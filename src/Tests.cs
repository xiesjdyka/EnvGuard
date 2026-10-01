using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ClaudeDesktopGuard.NativeCompute;
namespace EnvGuard
{
    internal static class Tests
    {
        static int checks;static void Assert(bool pass,string name){checks++;if(!pass)throw new Exception("FAIL: "+name);}
        internal static Profile Demo(string root)
        {
            var p=new Profile{UserSid="demo",Computer="示例设备",CapturedAt="示例确认时间",ExitIp="192.0.2.10",ProxyExecutable=Path.Combine(root,"proxy.exe"),ProxyHash=new string('A',64),LogFile=Path.Combine(root,"events.jsonl")};
            p.Settings=new Dictionary<string,string>{{"Windows 时区","Pacific Standard Time"},{"示例/语言","en-US"},{"示例/系统代理","已启用"},{"示例/夏令时","自动"}};
            p.Apps.Add(new AppTarget{Name="示例保护软件",Executable=Path.Combine(root,"app","Demo.exe"),Hash=new string('B',64),Folder=Path.Combine(root,"app")});return p;
        }
        public static int Run(string output)
        {
            string root=String.IsNullOrEmpty(output)?Path.Combine(Path.GetTempPath(),"EnvGuard-tests-"+Guid.NewGuid().ToString("N")):Path.GetFullPath(output);Directory.CreateDirectory(root);var report=new List<string>();int code=0;
            try{
                var p=Demo(root);p.Validate();Assert(true,"valid demo configuration");Assert(!Profile.SafeFolder(Path.GetPathRoot(root)),"reject drive root");Assert(!Profile.SafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),"reject user root");Assert(!Profile.Within(Path.Combine(root,"app-other","x.exe"),Path.Combine(root,"app")),"folder boundary");
                Assert(!Profile.LocalLogPath(@"\\server\share\events.jsonl"),"network log rejected");Assert(!Profile.LocalLogPath(@"C:events.jsonl"),"drive-relative log rejected");Assert(Profile.LocalLogPath(p.LogFile),"absolute local log accepted");
                var parent=new ProcessRecord{Pid=42,Created=100,Exited=300};var child=new ProcessRecord{Pid=43,Parent=42,Created=200};Assert(ProcessScope.ProvenChild(parent,child),"child lifetime proven");child.Created=400;Assert(!ProcessScope.ProvenChild(parent,child),"reused PID outside lifetime rejected");child.Created=50;Assert(!ProcessScope.ProvenChild(parent,child),"older unrelated child rejected");
                Assert(ProcessScope.SafeObservedTool(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","cmd.exe")),"known command child allowed only after attribution");Assert(!ProcessScope.SafeObservedTool(Path.Combine(root,"cmd.exe")),"tool exception requires exact system path");
                var policy=new AlertPolicy();var soft=EnvironmentChecker.Classify("192.0.2.10",new[]{(string)null,(string)null},new[]{"timeout","timeout"});Assert(!policy.Apply(soft),"single timeout no warning");Assert(policy.Apply(soft),"two consecutive timeouts warning");var ok=EnvironmentChecker.Classify("192.0.2.10",new[]{"192.0.2.10","192.0.2.10"},new string[2]);Assert(!policy.Apply(ok) && policy.SoftFailures==0,"healthy reset");var changed=EnvironmentChecker.Classify("192.0.2.10",new[]{"192.0.2.11",(string)null},new[]{(string)null,"timeout"});Assert(policy.Apply(changed),"one valid changed exit immediate");Assert(policy.NewIncident(new[]{"a"}),"new incident");Assert(!policy.NewIncident(new[]{"a"}),"deduplicate repeated incident");policy.NewIncident(new string[0]);Assert(policy.NewIncident(new[]{"a"}),"new incident after reset");
                using(var handler=EnvironmentChecker.Handler(p)){var destination=new Uri("https://api.ipify.org");Assert(handler.UseProxy && handler.Proxy.GetProxy(destination).AbsoluteUri=="http://127.0.0.1:10808/" && !handler.Proxy.IsBypassed(destination),"explicit proxy without bypass");Assert(!handler.AllowAutoRedirect && !handler.UseCookies,"no redirect or cookie fallback");}
                foreach(int mode in new[]{0,1,2,3})using(var fake=new FakeHandler(mode))using(var client=new HttpClient(fake)){
                    var network=new EnvironmentChecker(p).NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();Assert(fake.Requests==2,"two independent requests only, no retry fallback");
                    if(mode==0)Assert(network.Healthy,"response IP parsing");if(mode==1)Assert(network.Confirmed.Count>0 && network.Unconfirmed.Count>0,"partial probe failure preserves confirmed change");if(mode==2)Assert(!network.Healthy && network.Unconfirmed.Count==2,"redirect rejected as unconfirmed");if(mode==3)Assert(network.Unconfirmed.Count==2,"invalid response rejected");
                }
                string config=Path.Combine(root,"profile.json");p.Save(config);Assert(Profile.Load(config).ExitIp==p.ExitIp,"profile roundtrip");p.Settings["Windows 时区"]="other";p.Save(config);Assert(Profile.Load(config).Settings["Windows 时区"]=="other","atomic replacement");
                var audit=new AuditLog(Path.Combine(root,"audit.jsonl"));System.Threading.Tasks.Parallel.For(0,20,i=>AssertLog(audit.Write("test",new {i=i})));Assert(File.ReadAllLines(Path.Combine(root,"audit.jsonl")).Length==20,"concurrent append log");
                var blocked=new AuditLog(Path.Combine(root,"blocked","events.jsonl"));File.WriteAllText(Path.Combine(root,"blocked"),"not a directory");Assert(!blocked.Write("test",new{}) && blocked.Error!=null,"log failure visible");
                Assert(Services.Executable("\"C:\\Apps\\Example\\svc.exe\" --service")==@"C:\Apps\Example\svc.exe","quoted service path");Assert(Services.Executable(@"C:\Apps\Example\svc.exe --service")==@"C:\Apps\Example\svc.exe","unquoted service path");
                string registryTest=@"Software\EnvGuard\SnapshotSelfTest\"+Guid.NewGuid().ToString("N");try{
                    using(var key=Registry.CurrentUser.CreateSubKey(registryTest+@"\Rules")){key.SetValue("1","initial",RegistryValueKind.String);}
                    string first=EnvironmentChecker.Value(RegistryHive.CurrentUser,registryTest,"Rules");Assert(first.StartsWith("子键:"),"policy subkey detected");
                    using(var key=Registry.CurrentUser.CreateSubKey(registryTest+@"\Rules")){key.SetValue("1","changed",RegistryValueKind.String);}
                    Assert(first!=EnvironmentChecker.Value(RegistryHive.CurrentUser,registryTest,"Rules"),"policy subkey drift detected");
                }finally{if(!registryTest.StartsWith(@"Software\EnvGuard\SnapshotSelfTest\",StringComparison.Ordinal) || registryTest.Substring(registryTest.LastIndexOf('\\')+1).Length!=32)throw new Exception("unsafe test cleanup");Registry.CurrentUser.DeleteSubKeyTree(registryTest,false);}
                checks+=GuardComputeSystems.RunSelfTests();report.Add("PASS: pure policy, configuration, log, identity and Cowork parser checks");
                string fixture=Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),"Fixture.exe");
                if(File.Exists(fixture)){Fixture(root,fixture);report.Add("PASS: controlled parent, external child, orphan retention, unrelated process survival");}else throw new FileNotFoundException("Fixture.exe missing; run Build.ps1 -Test");
                EmergencyFixture(root,fixture);report.Add("PASS: actual manual emergency engine + request/result audit using fixture software only");
            }catch(Exception ex){code=1;report.Add(ex.ToString());}
            report.Add("Checks="+checks+"; ExitCode="+code);File.WriteAllLines(Path.Combine(root,"results.txt"),report);return code;
        }
        static void AssertLog(bool pass){if(!pass)throw new IOException("audit append failed");}
        sealed class FakeHandler : HttpMessageHandler
        {
            readonly int mode;public int Requests;public FakeHandler(int value){mode=value;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Interlocked.Increment(ref Requests);
                if(mode==1 && request.RequestUri.Host=="checkip.amazonaws.com")throw new TaskCanceledException("fixture timeout");
                return Task.FromResult(new HttpResponseMessage(mode==2?HttpStatusCode.Redirect:HttpStatusCode.OK){Content=new StringContent(mode==3?"not an IP":mode==1?"192.0.2.11":"192.0.2.10\n")});
            }
        }
        static void Fixture(string root,string fixture)
        {
            string own=Path.Combine(root,"owned"),external=Path.Combine(root,"external");Directory.CreateDirectory(own);Directory.CreateDirectory(external);
            string a=Path.Combine(own,"Parent.exe"),b=Path.Combine(external,"Child.exe"),other=Path.Combine(external,"Other.exe"),ready=Path.Combine(root,"ready.txt");File.Copy(fixture,a,true);File.Copy(fixture,b,true);File.Copy(fixture,other,true);
            var p=Demo(root);p.Apps=new List<AppTarget>{new AppTarget{Name="Owned test fixture",Executable=a,Hash=Profile.FileHash(a),Folder=own}};
            using(var scope=new ProcessScope(p))using(var unrelated=Process.Start(new ProcessStartInfo(other){UseShellExecute=false,CreateNoWindow=true}))using(var main=Process.Start(new ProcessStartInfo(a,"--parent \""+b+"\" \""+ready+"\""){UseShellExecute=false,CreateNoWindow=true})){
                int childPid=0;try{
                    var deadline=Stopwatch.StartNew();while(!File.Exists(ready) && deadline.ElapsedMilliseconds<4000)Thread.Sleep(50);Assert(File.Exists(ready),"fixture ready");childPid=Int32.Parse(File.ReadAllText(ready));scope.Refresh();Assert(scope.Live().Any(x=>x.Pid==main.Id) && scope.Live().Any(x=>x.Pid==childPid),"parent and child captured");Assert(!scope.Live().Any(x=>x.Pid==unrelated.Id),"unrelated excluded");
                    main.Kill();main.WaitForExit(2000);scope.Refresh();Assert(scope.Live().Any(x=>x.Pid==childPid),"orphan child retained");int count;Assert(scope.StopCurrent(out count).Count==0,"verified stop succeeds");deadline.Restart();while(scope.Live().Count>0 && deadline.ElapsedMilliseconds<3000)Thread.Sleep(50);Assert(scope.Live().Count==0,"owned processes terminated");Assert(!unrelated.HasExited,"unrelated survives");
                }finally{try{if(!main.HasExited)main.Kill();}catch{}try{if(!unrelated.HasExited)unrelated.Kill();}catch{}if(childPid>0)try{using(var child=Process.GetProcessById(childPid)){if(Profile.EqualPath(child.MainModule.FileName,b))child.Kill();}}catch{} }
            }
        }
        public static int UIShots(string directory)
        {
            Directory.CreateDirectory(directory);var p=Demo(directory);
            using(var wizard=new SetupForm(Path.Combine(directory,"never-saved.json"),p)){Shot(wizard,Path.Combine(directory,"setup.png"));wizard.PreviewPage(1,p);Shot(wizard,Path.Combine(directory,"environment.png"));Assert(!wizard.AdvancedShown,"optional checks collapsed initially");Assert(wizard.FieldsAligned,"setup input edges and heights aligned");wizard.PreviewAdvanced(true);Shot(wizard,Path.Combine(directory,"environment-advanced.png"));Assert(wizard.AdvancedShown,"optional checks explicitly expandable");wizard.PreviewPage(2,p);Shot(wizard,Path.Combine(directory,"baseline.png"));}
            using(var wizard=new SetupForm(Path.Combine(directory,"never-saved-scaled.json"),p)){wizard.PreviewPage(1,p);wizard.Scale(new SizeF(1.5f,1.5f));Shot(wizard,Path.Combine(directory,"environment-scaled.png"));Assert(wizard.FieldsAligned,"inputs aligned at simulated 150 percent scaling");}
            using(var main=new MainForm(p,"",true)){main.Render(new HealthState{Time=DateTimeOffset.Now,NetworkReady=true,ProcessCount=4},false);Shot(main,Path.Combine(directory,"monitor.png"));}
            var initial=new HealthState{Time=DateTimeOffset.Now,Issues=new[]{"网络检查连续两轮未确认。不是已确定代理掉线。","时区与确认的基准不同。"}};
            using(var alert=new AlertForm(initial,()=>System.Threading.Tasks.Task.FromResult(0))){Shot(alert,Path.Combine(directory,"warning.png"));alert.UpdateState(new HealthState{Time=DateTimeOffset.Now,NetworkReady=true});Shot(alert,Path.Combine(directory,"recovered.png"));}
            File.WriteAllText(Path.Combine(directory,"ui-result.txt"),"PASS: 4 UI layout/disclosure assertions, including simulated 150 percent scaling.\r\nSynthetic data only; no monitor, network probes or emergency actions started.");return 0;
        }
        static void EmergencyFixture(string root,string fixture)
        {
            string own=Path.Combine(root,"emergency-owned"),external=Path.Combine(root,"emergency-external");Directory.CreateDirectory(own);Directory.CreateDirectory(external);
            string a=Path.Combine(own,"Parent.exe"),b=Path.Combine(external,"Child.exe"),other=Path.Combine(external,"Other.exe"),ready=Path.Combine(root,"emergency-ready.txt");File.Copy(fixture,a,true);File.Copy(fixture,b,true);File.Copy(fixture,other,true);
            var p=Demo(root);p.LogFile=Path.Combine(root,"emergency.jsonl");p.Apps=new List<AppTarget>{new AppTarget{Name="Emergency fixture only",Executable=a,Hash=Profile.FileHash(a),Folder=own}};
            using(var unrelated=Process.Start(new ProcessStartInfo(other){UseShellExecute=false,CreateNoWindow=true}))using(var main=Process.Start(new ProcessStartInfo(a,"--parent \""+b+"\" \""+ready+"\""){UseShellExecute=false,CreateNoWindow=true})){
                int childPid=0;try{var clock=Stopwatch.StartNew();while(!File.Exists(ready) && clock.ElapsedMilliseconds<4000)Thread.Sleep(50);Assert(File.Exists(ready),"emergency fixture ready");childPid=Int32.Parse(File.ReadAllText(ready));
                    using(var engine=new MonitorEngine(p)){var result=engine.Emergency().GetAwaiter().GetResult();Assert(result.Success && result.Remaining==0,"manual emergency verified empty");Assert(result.StopAttempts>=2,"manual emergency includes child");Assert(!unrelated.HasExited,"manual emergency spares unrelated");var lines=File.ReadAllLines(p.LogFile);Assert(lines.Any(x=>x.Contains("emergency_requested")) && lines.Any(x=>x.Contains("emergency_result")),"manual emergency audit both events");}
                }finally{try{if(!main.HasExited)main.Kill();}catch{}try{if(!unrelated.HasExited)unrelated.Kill();}catch{}if(childPid>0)try{using(var child=Process.GetProcessById(childPid)){if(Profile.EqualPath(child.MainModule.FileName,b))child.Kill();}}catch{} }
            }
        }
        static void Shot(Form form,string path){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();form.PerformLayout();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
    }
}
