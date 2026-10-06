using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace EnvGuard
{
    public sealed class HealthState
    {
        public DateTimeOffset Time;
        public bool NetworkReady,NetworkPending;
        public int ProcessCount;
        public string[] Issues=new string[0];
        public string LogError;
        public bool Healthy {get{return Issues.Length==0 && NetworkReady && LogError==null;}}
        public string Summary {get{return LogError!=null?"日志写入失败":Issues.Length>0?"环境异常":NetworkPending?"网络暂未确认，正在复核":NetworkReady?"所选检查项与基准一致":"正在检测，请等待";}}
    }
    public sealed class AlertPolicy
    {
        public int SoftFailures {get;private set;}
        string signature="";
        public bool Apply(NetworkResult r){if(r.Healthy){SoftFailures=0;return false;}if(r.Unconfirmed.Count>0)SoftFailures++;return r.Confirmed.Count>0 || SoftFailures>=2;}
        public bool NewIncident(IEnumerable<string> issues){string next=String.Join("\n",issues.OrderBy(x=>x));bool notify=next.Length>0 && next!=signature;signature=next;return notify;}
    }
    public sealed class StopResult
    {
        public int StopAttempts,Remaining;
        public long ElapsedMs;
        public List<string> Errors=new List<string>();
        public string Coverage;
        public bool Success {get{return Remaining==0 && Errors.Count==0;}}
    }
    public static class Services
    {
        public static string CurrentPath(string name)
        {using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name))return key==null?null:Executable(Convert.ToString(key.GetValue("ImagePath")));}
        public static string Executable(string command)
        {
            if(String.IsNullOrWhiteSpace(command))return null;command=Environment.ExpandEnvironmentVariables(command.Trim());
            if(command.StartsWith("\"")){int end=command.IndexOf('"',1);return end>1?command.Substring(1,end-1):null;}
            int exe=command.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);return exe<0?null:command.Substring(0,exe+4);
        }
        public static List<ServiceTarget> Discover(string folder)
        {
            var found=new List<ServiceTarget>();using(var query=new ManagementObjectSearcher("SELECT Name,PathName FROM Win32_Service"))using(var result=query.Get())foreach(ManagementObject row in result)using(row){
                string file=Executable(Convert.ToString(row["PathName"]));if(file!=null && Profile.Within(file,folder) && !ProcessScope.IsSystemPath(file))try{found.Add(new ServiceTarget{Name=Convert.ToString(row["Name"]),Executable=file,Hash=Profile.FileHash(file)});}catch{}
            }return found;
        }
        public static void Verify(ServiceTarget target)
        {
            using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+target.Name)){
                if(key==null || !Profile.EqualPath(Executable(Convert.ToString(key.GetValue("ImagePath"))),target.Executable) || Profile.FileHash(target.Executable)!=target.Hash)throw new InvalidOperationException("服务身份与确认的配置不同，拒绝停止 "+target.Name);
            }
        }
        public static void Stop(ServiceTarget target)
        {
            Verify(target);using(var service=new ServiceController(target.Name)){
                service.Refresh();if(service.Status==ServiceControllerStatus.Stopped)return;
                // Never ask SCM to stop dependent services outside the chosen scope.
                if(service.DependentServices.Any(s=>s.Status!=ServiceControllerStatus.Stopped))throw new InvalidOperationException("服务有运行中的依赖方，拒绝扩大停止范围："+target.Name);
                if(service.Status!=ServiceControllerStatus.StopPending)service.Stop();service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(2));
            }
        }
    }
    public sealed class MonitorEngine : IDisposable
    {
        readonly Profile profile;readonly EnvironmentChecker checker;readonly ProcessScope scope;readonly AuditLog log;
        readonly CancellationTokenSource cancel=new CancellationTokenSource();readonly AlertPolicy policy=new AlertPolicy();
        readonly SemaphoreSlim operation=new SemaphoreSlim(1,1);
        Task worker;public event Action<HealthState,bool> Changed;
        public HealthState Latest {get;private set;}
        public MonitorEngine(Profile p){profile=p;checker=new EnvironmentChecker(p);scope=new ProcessScope(p);log=new AuditLog(p.LogFile);}
        public MonitorEngine(Profile p,string profileFile){profile=p;checker=new EnvironmentChecker(p);log=new AuditLog(p.LogFile);scope=new ProcessScope(p,new PackageUpdates(p,profileFile,log));}
        public void Start(){scope.StartEvents();log.Write("monitor_started",new {version=Program.Version,baseline=profile.CapturedAt,apps=profile.Apps.Select(a=>a.Name).ToArray()});worker=Task.Run((Func<Task>)Run);}
        async Task Run()
        {
            Task<NetworkResult> pending=null;NetworkResult last=null;bool networkWarning=false;DateTime next=DateTime.MinValue;DateTimeOffset? incidentAt=null;
            while(!cancel.IsCancellationRequested){
                try{
                    if(pending==null && DateTime.UtcNow>=next)pending=checker.Network(cancel.Token);
                    if(pending!=null && pending.IsCompleted){last=await pending.ConfigureAwait(false);pending=null;next=DateTime.UtcNow.AddSeconds(1);networkWarning=policy.Apply(last);
                        if(!last.Healthy)log.Write(networkWarning?"network_abnormal":"network_unconfirmed",new {confirmed=last.Confirmed,unconfirmed=last.Unconfirmed,addresses=last.Addresses,consecutive=policy.SoftFailures});
                    }
                    await operation.WaitAsync(cancel.Token).ConfigureAwait(false);List<string> issues;
                    try{issues=checker.Local();issues.AddRange(scope.Refresh());}finally{operation.Release();}
                    if(last!=null){issues.AddRange(last.Confirmed);if(networkWarning)issues.AddRange(last.Unconfirmed);}
                    var state=new HealthState{Time=DateTimeOffset.Now,Issues=issues.Distinct().ToArray(),ProcessCount=scope.Live().Count,NetworkReady=last!=null && last.Healthy,NetworkPending=last!=null && !last.Healthy && !networkWarning};
                    if(log.Error!=null)log.Write("log_recovery_probe",new {previous=log.Error});
                    state.LogError=log.Error;var alertIssues=state.Issues.ToList();if(state.LogError!=null)alertIssues.Add("日志无法保存："+state.LogError);
                    bool alert=policy.NewIncident(alertIssues);
                    if(alert){if(!incidentAt.HasValue)incidentAt=state.Time;log.Write("warning",new {causes=alertIssues,first=incidentAt});state.LogError=log.Error;}
                    else if(incidentAt.HasValue && state.Healthy){log.Write("recovered",new {first=incidentAt,restored=state.Time});incidentAt=null;state.LogError=log.Error;}
                    Latest=state;var handler=Changed;if(handler!=null)handler(state,alert);
                }catch(OperationCanceledException){break;}catch(Exception ex){log.Write("monitor_error",new {error=ex.Message});var state=new HealthState{Time=DateTimeOffset.Now,Issues=new[]{"检测程序异常："+ex.Message},LogError=log.Error};Latest=state;var h=Changed;if(h!=null)h(state,policy.NewIncident(state.Issues));}
                try{await Task.Delay(500,cancel.Token).ConfigureAwait(false);}catch(OperationCanceledException){break;}
            }
            if(pending!=null)try{await pending.ConfigureAwait(false);}catch{}
        }
        public string Inspect(){return String.Join("\r\n",scope.Live().Select(p=>p.OwnerName+" | PID "+p.Pid+" | "+p.Path));}
        public async Task<StopResult> Emergency()
        {
            var r=new StopResult{Coverage="结果仅覆盖配置的安装目录、已确认的进程后代和选定的专属服务；不代表网络隔离。"};var clock=Stopwatch.StartNew();
            await operation.WaitAsync().ConfigureAwait(false);
            try{
                if(!log.Write("emergency_requested",new {apps=profile.Apps.Select(a=>a.Name).ToArray()}))r.Errors.Add("紧急关闭请求日志写入失败："+log.Error);
                // Fast first wave: direct foreground and child processes, followed by service/HCS cleanup.
                r.Errors.AddRange(scope.Refresh(true));int count;r.Errors.AddRange(scope.StopCurrent(out count,profile.Apps.SelectMany(a=>a.Services).Select(s=>s.Executable)));r.StopAttempts+=count;
                var configured=profile.Apps.SelectMany(a=>a.Services).GroupBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
                var serviceErrors=await Task.WhenAll(configured.Select(s=>Task.Run(()=>{try{Services.Stop(s);return (string)null;}catch(Exception ex){return ex.Message;}}))).ConfigureAwait(false);r.Errors.AddRange(serviceErrors.Where(x=>x!=null));
                var cowork=configured.FirstOrDefault(s=>s.Name=="CoworkVMService");
                if(cowork!=null){var result=CoworkAdapter.Stop(cowork);r.Errors.AddRange(result);}
                var attemptedServices=new HashSet<string>(configured.Select(s=>s.Name+"\n"+s.Executable+"\n"+s.Hash),StringComparer.OrdinalIgnoreCase);
                int empty=0;while(clock.ElapsedMilliseconds<12000){
                    // Force a fresh package identity check at every emergency sweep;
                    // the normal three-second polling cache must not leave a new
                    // registered version outside this manual close request.
                    r.Errors.AddRange(scope.Refresh(true));r.Errors.AddRange(scope.StopCurrent(out count,profile.Apps.SelectMany(a=>a.Services).Select(s=>s.Executable)));r.StopAttempts+=count;
                    var reboundServices=profile.Apps.SelectMany(a=>a.Services).GroupBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Where(s=>attemptedServices.Add(s.Name+"\n"+s.Executable+"\n"+s.Hash)).ToArray();
                    foreach(var s in reboundServices)try{Services.Stop(s);if(s.Name=="CoworkVMService")r.Errors.AddRange(CoworkAdapter.Stop(s));}catch(Exception ex){r.Errors.Add(ex.Message);}
                    if(scope.Live().Count==0){empty++;if(empty>=4)break;}else empty=0;await Task.Delay(200).ConfigureAwait(false);
                }
                r.Remaining=scope.Live().Count;if(r.Remaining>0)r.Errors.Add("仍有 "+r.Remaining+" 个已识别进程存活。");
                foreach(var s in profile.Apps.SelectMany(a=>a.Services).GroupBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()))try{using(var service=new ServiceController(s.Name)){service.Refresh();if(service.Status!=ServiceControllerStatus.Stopped)r.Errors.Add(s.Name+" 仍未停止。");}}catch(Exception ex){r.Errors.Add(ex.Message);}
                r.Errors=r.Errors.Distinct().ToList();
            }catch(Exception ex){r.Errors.Add(ex.Message);}finally{r.ElapsedMs=clock.ElapsedMilliseconds;if(!log.Write("emergency_result",r))r.Errors.Add("紧急关闭结果日志写入失败："+log.Error);operation.Release();}
            return r;
        }
        public void Dispose(){cancel.Cancel();if(worker!=null)try{worker.Wait(6000);}catch{}scope.Dispose();log.Write("monitor_stopped",new {time=DateTimeOffset.Now});cancel.Dispose();}
    }
}
