using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace EnvGuard
{
    internal static class PackageUpdateTests
    {
        static int checks;
        static void Assert(bool pass,string name){checks++;if(!pass)throw new Exception("Package update FAIL: "+name);}
        static void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Assert(rejected,name);}
        sealed class Source : IRegisteredPackages
        {
            public List<RegisteredPackage> Packages=new List<RegisteredPackage>();public int Calls;
            public List<RegisteredPackage> Find(string family){Calls++;return Packages.ToList();}
        }
        sealed class Fixture
        {
            public Profile Profile;public Source Source=new Source();public PackageUpdates Updates;public string ServicePath;public RegisteredPackage Old,New;public string Log;
            public Fixture(string root,string fixture)
            {
                Directory.CreateDirectory(root);Old=Package(root,"1.0.0.0",fixture);New=Package(root,"2.0.0.0",fixture);
                string exe=Path.Combine(Old.Root,@"app\Fixture.exe"),svc=Path.Combine(Old.Root,@"app\resources\svc.exe");
                ServicePath=svc;Log=Path.Combine(root,"update-events.jsonl");
                Profile=Tests.Demo(root);Profile.Apps=new List<AppTarget>{new AppTarget{Name="Signed fixture",Executable=exe,Hash=EnvGuard.Profile.FileHash(exe),Folder=Path.GetDirectoryName(exe),
                    Package=new PackageBinding{Family=PackageUpdates.ClaudeFamily,Publisher=PackageUpdates.ClaudePublisher,FullName=Old.FullName,ExecutableRelative=@"app\Fixture.exe",FolderRelative="app"},
                    Services=new List<ServiceTarget>{new ServiceTarget{Name="SelectedFixtureService",Executable=svc,Hash=EnvGuard.Profile.FileHash(svc),PackageRelative=@"app\resources\svc.exe"}}}};
                Source.Packages.Add(Old);Updates=new PackageUpdates(Profile,null,new AuditLog(Log),Source,name=>ServicePath);
            }
            public void Upgrade(){Source.Packages=new List<RegisteredPackage>{New};ServicePath=Path.Combine(New.Root,@"app\resources\svc.exe");}
        }
        static RegisteredPackage Package(string root,string version,string fixture)
        {
            string full="Claude_"+version+"_x64__pzs8sxrjxfjjc",folder=Path.Combine(root,full);Directory.CreateDirectory(Path.Combine(folder,@"app\resources"));
            File.Copy(fixture,Path.Combine(folder,@"app\Fixture.exe"),true);File.Copy(fixture,Path.Combine(folder,@"app\Helper.exe"),true);File.WriteAllText(Path.Combine(folder,@"app\resources\svc.exe"),"fixture service "+version);
            return new RegisteredPackage{Family=PackageUpdates.ClaudeFamily,Publisher=PackageUpdates.ClaudePublisher,FullName=full,Version=Version.Parse(version),Root=folder,Origin=5,Executables=new List<string>{@"app\Fixture.exe"}};
        }
        internal static int Run(string root,string fixture)
        {
            checks=0;
            var f=new Fixture(Path.Combine(root,"binding-roundtrip"),fixture);
            var baseline=new JavaScriptSerializer().Serialize(f.Profile.Settings);string captured=f.Profile.CapturedAt,exit=f.Profile.ExitIp,proxy=f.Profile.ProxyExecutable;int port=f.Profile.ProxyPort;
            Assert(f.Updates.Refresh(true).Count==0,"initial binding healthy");int calls=f.Source.Calls;f.Updates.Refresh();Assert(f.Source.Calls==calls,"native enumeration throttled, no busy poll");
            f.Upgrade();Assert(f.Updates.Refresh(true).Count==0,"signed update rebound");
            Assert(f.Profile.Apps[0].Package.FullName==f.New.FullName && EnvGuard.Profile.Within(f.Profile.Apps[0].Executable,f.New.Root),"new executable and family retained");
            Assert(f.Profile.Apps[0].Services.Count==1 && f.Profile.Apps[0].Services[0].Name=="SelectedFixtureService" && EnvGuard.Profile.EqualPath(f.Profile.Apps[0].Services[0].Executable,f.ServicePath),"only selected service follows verified version");
            Assert(f.Profile.ExitIp==exit && f.Profile.ProxyPort==port && f.Profile.ProxyExecutable==proxy && f.Profile.CapturedAt==captured && new JavaScriptSerializer().Serialize(f.Profile.Settings)==baseline,"network/time/user baseline never recaptured");
            Assert(f.Updates.Coverage.Any(x=>EnvGuard.Profile.Within(x.Executable,f.Old.Root)),"old-version kill coverage retained");
            var events=File.ReadAllLines(f.Log);Assert(events.Any(x=>x.Contains("app_update_binding_prepared"))&&events.Any(x=>x.Contains("app_update_rebound")),"prepare and result audit");
            int lines=events.Length;Assert(f.Updates.Refresh(true).Count==0 && File.ReadAllLines(f.Log).Length==lines,"unchanged version has no repeated rebind logs");
            f.Profile.Apps[0]=new JavaScriptSerializer().Deserialize<AppTarget>(new JavaScriptSerializer().Serialize(f.Profile.Apps[0]));Assert(f.Updates.Refresh(true).Count==0 && File.ReadAllLines(f.Log).Length==lines,"JSON roundtrip property order does not trigger false rebind");
            foreach(string relative in new[]{@"..\other.exe",@"app\..\other.exe",@"C:\other.exe",@"app\\other.exe",@"app\file.exe:stream"})Reject(()=>PackageUpdates.Relative(relative),"relative traversal or stream rejected");
            foreach(int origin in new[]{0,1,2,4})Assert(!new RegisteredPackage{Origin=origin}.Trusted,"untrusted package origin rejected");
            Assert(!new RegisteredPackage{Origin=5,Properties=0x10000}.Trusted,"development registration rejected");
            foreach(string kind in new[]{"publisher","family","unsigned","development","entry","service","missing","same-version-hash"}){
                var bad=new Fixture(Path.Combine(root,"binding-reject-"+kind),fixture);bad.Updates.Refresh(true);string before=bad.Profile.Apps[0].Executable;
                if(kind=="same-version-hash")File.AppendAllText(before,"tamper");else{bad.Upgrade();
                    if(kind=="publisher")bad.New.Publisher="CN=Other publisher";
                    if(kind=="family")bad.New.Family="Other_pzs8sxrjxfjjc";
                    if(kind=="unsigned")bad.New.Origin=1;
                    if(kind=="development")bad.New.Properties=0x10000;
                    if(kind=="entry")bad.New.Executables.Clear();
                    if(kind=="service")bad.ServicePath=Path.Combine(root,"unrelated-service.exe");
                    if(kind=="missing")bad.Source.Packages.Clear();
                }
                Assert(bad.Updates.Refresh(true).Count>0 && bad.Profile.Apps[0].Executable==before,"unsafe/partial update does not replace saved authority: "+kind);
            }
            var writeFail=new Fixture(Path.Combine(root,"binding-save-failure"),fixture);writeFail.Upgrade();string blocked=Path.Combine(root,"not-a-directory");File.WriteAllText(blocked,"fixture");
            var failed=new PackageUpdates(writeFail.Profile,Path.Combine(blocked,"profile.json"),new AuditLog(writeFail.Log),writeFail.Source,name=>writeFail.ServicePath);
            Assert(failed.Refresh(true).Count>0 && writeFail.Profile.Apps[0].Package.FullName==writeFail.Old.FullName,"persistence failure does not mutate live profile");
            var noFolder=new Fixture(Path.Combine(root,"binding-no-folder"),fixture);noFolder.Profile.Apps[0].Folder=null;noFolder.Profile.Apps[0].Services.Clear();noFolder.Profile.Apps[0].Package.FolderRelative=null;noFolder.Upgrade();Assert(noFolder.Updates.Refresh(true).Count==0 && noFolder.Profile.Apps[0].Folder==null && noFolder.Updates.Coverage.All(x=>x.Folder==null),"EXE-only selection never grows into directory scope");
            var legacy=new Fixture(Path.Combine(root,"binding-legacy"),fixture);string store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps"),old=Path.Combine(store,"Claude_2.16120.0.0_x64__pzs8sxrjxfjjc");
            var oldTarget=new AppTarget{Name="claude",Executable=Path.Combine(old,@"app\claude.exe"),Folder=Path.Combine(old,"app"),Hash=new string('A',64)};
            Assert(legacy.Updates.Legacy(oldTarget).Family==PackageUpdates.ClaudeFamily,"missing legacy Claude has explicitly pinned migration identity");
            oldTarget.Executable=Path.Combine(root,"claude.exe");Assert(legacy.Updates.Legacy(oldTarget)==null,"name alone never opts into migration");
            Assert(WindowsPackages.FamilyOf("Claude_2.19675.0.0_x64__pzs8sxrjxfjjc")==PackageUpdates.ClaudeFamily,"native family extraction is version independent");
            LiveUpgrade(Path.Combine(root,"binding-live-processes"),fixture);
            return checks;
        }
        static void LiveUpgrade(string root,string fixture)
        {
            var f=new Fixture(root,fixture);string external=Path.Combine(root,"outside");Directory.CreateDirectory(external);string child=Path.Combine(external,"Child.exe"),other=Path.Combine(external,"Other.exe"),ready=Path.Combine(root,"child-ready.txt");File.Copy(fixture,child);File.Copy(fixture,other);
            int childPid=0;Process next=null;
            using(var scope=new ProcessScope(f.Profile,f.Updates))using(var unrelated=Process.Start(new ProcessStartInfo(other){UseShellExecute=false,CreateNoWindow=true}))using(var old=Process.Start(new ProcessStartInfo(f.Profile.Apps[0].Executable,"--parent \""+child+"\" \""+ready+"\""){UseShellExecute=false,CreateNoWindow=true})){
                try{
                    var clock=Stopwatch.StartNew();while(!File.Exists(ready)&&clock.ElapsedMilliseconds<5000)Thread.Sleep(50);Assert(File.Exists(ready),"old fixture child ready");childPid=Int32.Parse(File.ReadAllText(ready));scope.Refresh();
                    f.Upgrade();Assert(f.Updates.Refresh(true).Count==0,"live signed update binds");next=Process.Start(new ProcessStartInfo(Path.Combine(f.New.Root,@"app\Fixture.exe")){UseShellExecute=false,CreateNoWindow=true});scope.Refresh();
                    Assert(scope.Live().Any(x=>x.Pid==old.Id)&&scope.Live().Any(x=>x.Pid==next.Id)&&scope.Live().Any(x=>x.Pid==childPid),"old/new versions and external observed child covered together");
                    Assert(!scope.Live().Any(x=>x.Pid==unrelated.Id),"same fixture binary outside scope remains excluded");
                    old.Kill();old.WaitForExit(2000);scope.Refresh();Assert(scope.Live().Any(x=>x.Pid==childPid),"pre-update orphan retained after parent exits");
                    int stopped;Assert(scope.StopCurrent(out stopped).Count==0 && stopped>=2,"verified stop includes new version and old orphan");
                    clock.Restart();while(scope.Live().Count>0&&clock.ElapsedMilliseconds<3000)Thread.Sleep(50);Assert(scope.Live().Count==0&&!unrelated.HasExited,"update close sweep verified empty, unrelated survived");
                }finally{if(next!=null){try{if(!next.HasExited)next.Kill();}catch{}next.Dispose();}try{if(!old.HasExited)old.Kill();}catch{}try{if(!unrelated.HasExited)unrelated.Kill();}catch{}if(childPid>0)try{using(var orphan=Process.GetProcessById(childPid)){if(EnvGuard.Profile.EqualPath(orphan.MainModule.FileName,child))orphan.Kill();}}catch{} }
            }
        }
    }
}
