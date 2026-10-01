using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Management;

namespace EnvGuard
{
    public sealed class ProcessRecord : IDisposable
    {
        public int Pid, Parent;
        public long Created, Exited;
        public string Name, Path, OwnerName;
        public IntPtr Handle;
        public bool Owned;
        public string Identity { get {return Pid+":"+Created;} }
        public bool Alive {get{Refresh();return Exited==0 && Handle!=IntPtr.Zero;}}
        public void Refresh(){long c,e,k,u;if(Handle!=IntPtr.Zero && Native.GetProcessTimes(Handle,out c,out e,out k,out u) && c==Created)Exited=e;else Exited=-1;}
        public void Dispose(){if(Handle!=IntPtr.Zero){Native.CloseHandle(Handle);Handle=IntPtr.Zero;}}
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct Entry
        {public uint Size,Usage,Pid;public IntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;}
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool Process32FirstW(IntPtr snapshot,ref Entry entry);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool Process32NextW(IntPtr snapshot,ref Entry entry);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern IntPtr OpenProcess(uint rights,bool inherit,int pid);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int length);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")]internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]internal static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
        [DllImport("iphlpapi.dll",SetLastError=true)]internal static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool order,int family,int type,uint reserved);
        internal static List<ProcessRecord> Snapshot()
        {
            var records=new List<ProcessRecord>(); long boundary=DateTime.UtcNow.ToFileTimeUtc();
            IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
            if(snapshot==new IntPtr(-1))throw new Win32Exception();
            try{
                var entry=new Entry{Size=(uint)Marshal.SizeOf(typeof(Entry))};
                bool more=Process32FirstW(snapshot,ref entry);
                while(more){
                    if(entry.Pid>0){var record=Read((int)entry.Pid,(int)entry.Parent,entry.Name);if(record!=null){if(record.Created>boundary)record.Dispose();else records.Add(record);}}
                    more=Process32NextW(snapshot,ref entry);
                }
            }finally{CloseHandle(snapshot);}return records;
        }
        internal static ProcessRecord Read(int pid,int parent,string name)
        {
            var r=new ProcessRecord{Pid=pid,Parent=parent,Name=name};r.Handle=OpenProcess(0x1000|0x100000,false,pid);
            if(r.Handle==IntPtr.Zero){if(Marshal.GetLastWin32Error()==87)return null;return r;}
            long c,e,k,u;if(GetProcessTimes(r.Handle,out c,out e,out k,out u)){r.Created=c;r.Exited=e;}
            var path=new StringBuilder(32768);int size=path.Capacity;if(QueryFullProcessImageName(r.Handle,0,path,ref size)){r.Path=path.ToString();r.Name=System.IO.Path.GetFileName(r.Path);}
            if(r.Exited>0){r.Dispose();return null;}return r;
        }
        internal static int ListenerPid(int port)
        {
            int size=0;GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,3,0);IntPtr memory=Marshal.AllocHGlobal(size);
            try{
                uint error=GetExtendedTcpTable(memory,ref size,false,2,3,0);if(error!=0)throw new Win32Exception((int)error);
                int count=Marshal.ReadInt32(memory);for(int i=0;i<count;i++){IntPtr row=IntPtr.Add(memory,4+i*24);int local=Marshal.ReadInt32(row,8);int localPort=((local&255)<<8)|((local>>8)&255);int state=Marshal.ReadInt32(row);int address=Marshal.ReadInt32(row,4);
                    if(state==2 && localPort==port && (address==0 || address==0x0100007f))return Marshal.ReadInt32(row,20);
                }return 0;
            }finally{Marshal.FreeHGlobal(memory);}
        }
        internal static void Stop(ProcessRecord record)
        {
            record.Refresh();if(record.Exited>0)return;if(record.Exited<0 || record.Created==0 || record.Path==null)throw new InvalidOperationException("进程身份无法验证");
            IntPtr handle=OpenProcess(0x1000|1|0x100000,false,record.Pid);if(handle==IntPtr.Zero)throw new Win32Exception();
            try{long c,e,k,u;var path=new StringBuilder(32768);int len=path.Capacity;
                if(!GetProcessTimes(handle,out c,out e,out k,out u) || c!=record.Created || !QueryFullProcessImageName(handle,0,path,ref len) || !Profile.EqualPath(path.ToString(),record.Path))throw new InvalidOperationException("进程身份已改变");
                if(e==0 && !TerminateProcess(handle,125) && WaitForSingleObject(handle,0)!=0)throw new Win32Exception();
            }finally{CloseHandle(handle);}
        }
    }
    public sealed class ProcessScope : IDisposable
    {
        readonly Profile profile;
        readonly Dictionary<string,ProcessRecord> owned=new Dictionary<string,ProcessRecord>();
        readonly object gate=new object();
        readonly HashCache hashes=new HashCache();
        ManagementEventWatcher watcher;
        public string TrackingWarning {get;private set;}
        public ProcessScope(Profile config){profile=config;}
        public void StartEvents()
        {
            try {watcher=new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                watcher.EventArrived+=(s,e)=>{try{var p=Native.Read(Convert.ToInt32(e.NewEvent["ProcessID"]),Convert.ToInt32(e.NewEvent["ParentProcessID"]),Convert.ToString(e.NewEvent["ProcessName"]));if(p==null)return;
                    lock(gate){foreach(var a in owned.Values)a.Refresh();var parent=owned.Values.FirstOrDefault(a=>ProvenChild(a,p));
                        if(parent!=null && p.Path!=null && !NeverStop(p,true) && !owned.ContainsKey(p.Identity)){p.Owned=true;p.OwnerName=parent.OwnerName;owned.Add(p.Identity,p);}else p.Dispose();}
                }catch(Exception){TrackingWarning="进程启动事件读取失败；当前仅定期扫描。";}};
                watcher.Start();
            }catch(Exception ex){TrackingWarning="进程启动事件不可用，仅定期扫描："+ex.Message;}
        }
        public static bool IsSystemPath(string path)
        {return String.IsNullOrWhiteSpace(path) || Profile.Within(path,Environment.GetFolderPath(Environment.SpecialFolder.Windows));}
        public static bool ProvenChild(ProcessRecord parent,ProcessRecord child)
        {return child.Parent==parent.Pid && parent.Created>0 && child.Created>parent.Created && (parent.Exited==0 || (parent.Exited>0 && child.Created<=parent.Exited));}
        public static bool SafeObservedTool(string path)
        {
            if(String.IsNullOrEmpty(path))return false;string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach(string directory in new[]{"System32","SysWOW64"})foreach(string relative in new[]{"cmd.exe",@"WindowsPowerShell\v1.0\powershell.exe"})if(Profile.EqualPath(path,Path.Combine(windows,directory,relative)))return true;return false;
        }
        static bool NeverStop(ProcessRecord p,bool child=false)
        {
            string[] names={"System","Registry","csrss.exe","lsass.exe","services.exe","wininit.exe","winlogon.exe","svchost.exe","vmcompute.exe","vmms.exe","vmwp.exe","vmmem","vmmemWSL","wslservice.exe","wslhost.exe","wslrelay.exe","explorer.exe"};
            return p.Pid==Process.GetCurrentProcess().Id || names.Contains(p.Name,StringComparer.OrdinalIgnoreCase) || (p.Path!=null && IsSystemPath(p.Path) && !(child && SafeObservedTool(p.Path)));
        }
        public List<string> Refresh()
        {
            lock(gate){
                var errors=new List<string>();var fresh=Native.Snapshot();
                foreach(var anchor in owned.Values)anchor.Refresh();
                var valid=new HashSet<AppTarget>();
                foreach(var app in profile.Apps)try{if(hashes.Read(app.Executable)==app.Hash)valid.Add(app);else errors.Add(app.Name+" 程序已更新或改变，需要重新确认配置。");}catch(Exception){errors.Add(app.Name+" 程序身份无法读取。");}
                foreach(var p in fresh){
                    if(owned.ContainsKey(p.Identity)){p.Dispose();continue;}
                    if(p.Path!=null && !NeverStop(p)){
                        var app=valid.FirstOrDefault(a=>Profile.EqualPath(p.Path,a.Executable) || Profile.Within(p.Path,a.Folder));
                        if(app!=null){p.Owned=true;p.OwnerName=app.Name;owned.Add(p.Identity,p);}
                    }else if(p.Path==null && profile.Apps.Any(a=>String.Equals(Path.GetFileName(a.Executable),p.Name,StringComparison.OrdinalIgnoreCase)))errors.Add("无法读取目标进程 PID "+p.Pid+" 的身份。");
                }
                bool changed;do{changed=false;foreach(var p in fresh.Where(x=>!x.Owned && x.Handle!=IntPtr.Zero)){
                    var parent=owned.Values.FirstOrDefault(a=>ProvenChild(a,p));if(parent==null)continue;
                    if(NeverStop(p,true)){errors.Add("保留共享／系统子进程 PID "+p.Pid+" "+p.Name);continue;}
                    if(p.Path==null){errors.Add("无法识别子进程 PID "+p.Pid);continue;}
                    p.Owned=true;p.OwnerName=parent.OwnerName;owned[p.Identity]=p;changed=true;
                }}while(changed);
                foreach(var p in fresh)if(!p.Owned && p.Handle!=IntPtr.Zero)p.Dispose();
                // Retain ancestors transitively while any observed descendant is alive.
                var keep=new HashSet<string>(owned.Values.Where(x=>x.Exited==0).Select(x=>x.Identity));
                bool added;do{added=false;foreach(var p in owned.Values)if(!keep.Contains(p.Identity) && owned.Values.Any(c=>keep.Contains(c.Identity) && ProvenChild(p,c))){keep.Add(p.Identity);added=true;}}while(added);
                foreach(var key in owned.Where(pair=>pair.Value.Exited>0 && !keep.Contains(pair.Key)).Select(pair=>pair.Key).ToArray()){owned[key].Dispose();owned.Remove(key);}
                if(!String.IsNullOrEmpty(TrackingWarning))errors.Add(TrackingWarning);
                return errors.Distinct().ToList();
            }
        }
        public List<ProcessRecord> Live(){lock(gate){foreach(var p in owned.Values)p.Refresh();return owned.Values.Where(p=>p.Exited==0).ToList();}}
        public List<string> StopCurrent(out int count)
        {lock(gate){count=0;var errors=new List<string>();foreach(var p in owned.Values.ToArray()){try{if(p.Alive){Native.Stop(p);count++;}}catch(Exception ex){errors.Add(p.Name+" PID "+p.Pid+"："+ex.Message);}}return errors;}}
        public void Dispose(){if(watcher!=null){try{watcher.Stop();}catch{}watcher.Dispose();watcher=null;}lock(gate){foreach(var p in owned.Values)p.Dispose();owned.Clear();}}
    }
}
