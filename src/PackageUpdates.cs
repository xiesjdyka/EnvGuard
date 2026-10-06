// Native AppModel APIs only: no per-poll PowerShell, directory glob or name-only trust.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace EnvGuard
{
    internal sealed class RegisteredPackage
    {
        public string Family,FullName,Root,Publisher;
        public Version Version;
        public uint Properties;
        public int Origin;
        public List<string> Executables=new List<string>();
        // Reject unsigned/development registrations even when the name looks right.
        public bool Trusted {get{return (Properties&0x10000)==0 && (Origin==3 || Origin==5 || Origin==6);}}
    }
    internal interface IRegisteredPackages {List<RegisteredPackage> Find(string family);}
    internal sealed class WindowsPackages : IRegisteredPackages
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int FindPackagesByPackageFamily(string family,uint filters,ref uint count,IntPtr names,ref uint length,IntPtr buffer,IntPtr properties);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int PackageFamilyNameFromFullName(string fullName,ref uint length,StringBuilder family);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackagePathByFullName(string fullName,ref uint length,StringBuilder path);
        [DllImport("kernelbase.dll",CharSet=CharSet.Unicode)]static extern int GetStagedPackageOrigin(string fullName,out int origin);
        internal static string FamilyOf(string fullName)
        {
            uint size=0;int error=PackageFamilyNameFromFullName(fullName,ref size,null);
            if(error!=122 || size<2 || size>1024)throw new InvalidDataException("应用包名称无效。");
            var value=new StringBuilder((int)size);error=PackageFamilyNameFromFullName(fullName,ref size,value);
            if(error!=0)throw new Win32Exception(error);return value.ToString();
        }
        static string RootOf(string fullName)
        {
            uint size=0;int error=GetPackagePathByFullName(fullName,ref size,null);
            if(error!=122 || size<2 || size>32768)throw new InvalidDataException("已登记的应用包路径无法读取。");
            var value=new StringBuilder((int)size);error=GetPackagePathByFullName(fullName,ref size,value);
            if(error!=0)throw new Win32Exception(error);
            string root=value.ToString(),store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps");
            if(!Profile.Within(root,store) || !Profile.EqualPath(Path.GetDirectoryName(root),store))throw new InvalidDataException("应用包不在 Windows 管理的专属目录内。");
            PackageUpdates.NoLinks(root,root);return root;
        }
        public List<RegisteredPackage> Find(string family)
        {
            // HEAD excludes resource/framework dependencies. Returned registrations
            // belong to the current Windows user, not merely staged disk folders.
            uint count=0,length=0;int error=FindPackagesByPackageFamily(family,0x10,ref count,IntPtr.Zero,ref length,IntPtr.Zero,IntPtr.Zero);
            if(error==0 && count==0)return new List<RegisteredPackage>();
            if(error!=122 || count>128 || length>131072)throw new InvalidDataException("应用包登记暂时无法确认。");
            IntPtr names=Marshal.AllocHGlobal((int)count*IntPtr.Size),buffer=Marshal.AllocHGlobal((int)length*2),properties=Marshal.AllocHGlobal((int)count*4);
            try{
                error=FindPackagesByPackageFamily(family,0x10,ref count,names,ref length,buffer,properties);
                if(error!=0)throw new Win32Exception(error);var result=new List<RegisteredPackage>();
                for(int i=0;i<count;i++){
                    string full=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));
                    if(!String.Equals(FamilyOf(full),family,StringComparison.Ordinal))throw new InvalidDataException("登记的应用身份不匹配。");
                    int origin;error=GetStagedPackageOrigin(full,out origin);if(error!=0)throw new Win32Exception(error);
                    var package=new RegisteredPackage{Family=family,FullName=full,Root=RootOf(full),Properties=unchecked((uint)Marshal.ReadInt32(properties,i*4)),Origin=origin};
                    if(!package.Trusted)throw new InvalidDataException("应用包未签名或处于开发模式，拒绝自动接续。");
                    string manifest=Path.Combine(package.Root,"AppxManifest.xml");PackageUpdates.NoLinks(package.Root,manifest);
                    var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024};var doc=new XmlDocument{XmlResolver=null};
                    using(var reader=XmlReader.Create(manifest,settings))doc.Load(reader);
                    var identity=(XmlElement)doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
                    if(identity==null || identity.GetAttribute("Name")!=family.Substring(0,family.LastIndexOf('_')))throw new InvalidDataException("应用清单身份与登记不同。");
                    package.Publisher=identity.GetAttribute("Publisher");package.Version=Version.Parse(identity.GetAttribute("Version"));
                    if(!full.StartsWith(identity.GetAttribute("Name")+"_"+package.Version+"_",StringComparison.Ordinal))throw new InvalidDataException("清单版本与登记不同。");
                    foreach(XmlElement application in doc.SelectNodes("//*[local-name()='Applications']/*[local-name()='Application']")){
                        string relative=application.GetAttribute("Executable").Replace('/','\\');
                        if(!String.IsNullOrEmpty(relative) && relative.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){PackageUpdates.Relative(relative);package.Executables.Add(relative);}
                    }
                    result.Add(package);
                }
                return result;
            }finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);Marshal.FreeHGlobal(properties);}
        }
    }
    public sealed class PackageUpdates
    {
        internal const string ClaudeFamily="Claude_pzs8sxrjxfjjc";
        internal const string ClaudePublisher="CN=\"Anthropic, PBC\", O=\"Anthropic, PBC\", L=San Francisco, S=California, C=US, SERIALNUMBER=4860621, OID.2.5.4.15=Private Organization, OID.1.3.6.1.4.1.311.60.2.1.2=Delaware, OID.1.3.6.1.4.1.311.60.2.1.3=US";
        readonly Profile profile;readonly string profileFile;readonly AuditLog audit;readonly IRegisteredPackages source;
        readonly Func<string,string> servicePath;readonly List<AppTarget> retired=new List<AppTarget>();
        readonly Dictionary<string,AppTarget> versions=new Dictionary<string,AppTarget>(StringComparer.Ordinal);
        readonly HashCache hashes=new HashCache();DateTime next=DateTime.MinValue;List<string> last=new List<string>();
        public PackageUpdates(Profile p,string file,AuditLog log):this(p,file,log,new WindowsPackages(),Services.CurrentPath){}
        internal PackageUpdates(Profile p,string file,AuditLog log,IRegisteredPackages packages,Func<string,string> serviceReader){profile=p;profileFile=String.IsNullOrWhiteSpace(file)?null:file;audit=log;source=packages;servicePath=serviceReader;}
        internal static void Relative(string value)
        {
            if(String.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.IndexOf(':')>=0 || value.Split('\\','/').Any(x=>x==".." || x=="." || x.Length==0))throw new InvalidDataException("应用包内相对路径无效。");
        }
        internal static string Resolve(string root,string relative){Relative(relative);string path=Path.GetFullPath(Path.Combine(root,relative));if(!Profile.Within(path,root))throw new InvalidDataException("路径超出应用包。");return path;}
        internal static void NoLinks(string root,string path)
        {
            string current=Path.GetFullPath(path),boundary=Path.GetFullPath(root);
            if(!Profile.EqualPath(current,boundary) && !Profile.Within(current,boundary))throw new InvalidDataException("路径超出应用包。");
            while(true){if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("应用包路径含重定向，拒绝接续。");if(Profile.EqualPath(current,boundary))break;current=Path.GetDirectoryName(current);}
        }
        internal static string InPackage(string path,string root){if(!Profile.Within(path,root))throw new InvalidDataException("原范围不属于应用包。");string relative=Path.GetFullPath(path).Substring(Path.GetFullPath(root).TrimEnd('\\').Length+1);Relative(relative);return relative;}
        internal static string ExistingRoot(string path)
        {
            string store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps");
            if(!Profile.Within(path,store))return null;string relative=Path.GetFullPath(path).Substring(store.Length+1);int slash=relative.IndexOf('\\');
            return slash>0?Path.Combine(store,relative.Substring(0,slash)):null;
        }
        public static void ValidateBinding(AppTarget app)
        {
            var b=app.Package;if(String.IsNullOrWhiteSpace(b.Family) || !Regex.IsMatch(b.Family,@"\A[A-Za-z0-9.-]+_[a-z0-9]{13}\z") || String.IsNullOrWhiteSpace(b.Publisher) || b.Publisher.Length>2048 || String.IsNullOrWhiteSpace(b.FullName))throw new InvalidDataException("自动接续的应用身份不完整。");
            Relative(b.ExecutableRelative);if(b.FolderRelative!=null)Relative(b.FolderRelative);
            if(!String.Equals(WindowsPackages.FamilyOf(b.FullName),b.Family,StringComparison.Ordinal))throw new InvalidDataException("自动接续的应用包身份不一致。");
            string root=ExistingRoot(app.Executable);if(root==null || Path.GetFileName(root)!=b.FullName || !Profile.EqualPath(Resolve(root,b.ExecutableRelative),app.Executable) || (app.Folder!=null && !Profile.EqualPath(Resolve(root,b.FolderRelative),app.Folder)) || (app.Folder==null && b.FolderRelative!=null))throw new InvalidDataException("接续路径与已确认范围不一致。");
            foreach(var service in app.Services){Relative(service.PackageRelative);if(!Profile.Within(service.Executable,root)){
                    // A service may briefly retain a previous, same-family version
                    // while the app registration updates. Verify that family too.
                    string serviceRoot=ExistingRoot(service.Executable);if(serviceRoot==null || WindowsPackages.FamilyOf(Path.GetFileName(serviceRoot))!=b.Family || !Profile.EqualPath(Resolve(serviceRoot,service.PackageRelative),service.Executable))throw new InvalidDataException("服务不属于绑定的应用包。");
                }else if(!Profile.EqualPath(Resolve(root,service.PackageRelative),service.Executable))throw new InvalidDataException("服务相对路径发生改变。");}
        }
        static PackageBinding Clone(PackageBinding b){return new PackageBinding{Family=b.Family,Publisher=b.Publisher,FullName=b.FullName,ExecutableRelative=b.ExecutableRelative,FolderRelative=b.FolderRelative};}
        internal PackageBinding Legacy(AppTarget app)
        {
            string root=ExistingRoot(app.Executable);if(root==null)return null;
            string family=WindowsPackages.FamilyOf(Path.GetFileName(root));
            // Old profiles stored no signer. Only migrate the explicitly pinned
            // official Claude family/executable, never an arbitrary missing EXE.
            if(family!=ClaudeFamily || !Profile.EqualPath(app.Executable,Path.Combine(root,@"app\claude.exe")))return null;
            return new PackageBinding{Family=family,Publisher=ClaudePublisher,FullName=Path.GetFileName(root),ExecutableRelative=@"app\claude.exe",FolderRelative=app.Folder==null?null:InPackage(app.Folder,root)};
        }
        public static bool BindSelected(AppTarget app)
        {
            string root=ExistingRoot(app.Executable);if(root==null)return false;string full=Path.GetFileName(root),family=WindowsPackages.FamilyOf(full);
            var package=new WindowsPackages().Find(family).SingleOrDefault(x=>x.FullName==full);
            if(package==null || !package.Trusted)throw new InvalidDataException("选定软件不是可验证的签名应用包。");
            string relative=InPackage(app.Executable,package.Root);
            if(!package.Executables.Contains(relative,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("所选 EXE 不是应用包清单中的入口。");
            NoLinks(package.Root,app.Executable);
            app.Package=new PackageBinding{Family=family,Publisher=package.Publisher,FullName=full,ExecutableRelative=relative,FolderRelative=app.Folder==null?null:InPackage(app.Folder,root)};
            foreach(var service in app.Services)service.PackageRelative=InPackage(service.Executable,root);
            return true;
        }
        internal AppTarget Plan(AppTarget app,PackageBinding binding,List<RegisteredPackage> packages)
        {
            if(packages.Count==0)throw new InvalidDataException("绑定的软件尚未完成安装或已卸载。");
            foreach(var package in packages)if(!package.Trusted || package.Family!=binding.Family || package.Publisher!=binding.Publisher)throw new InvalidDataException("应用签名来源／发布者与绑定身份不同。");
            var current=packages.OrderByDescending(x=>x.Version).ThenBy(x=>x.FullName,StringComparer.Ordinal).First();
            if(!current.Executables.Contains(binding.ExecutableRelative,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("新版清单没有原来的软件入口，需人工确认。");
            string executable=Resolve(current.Root,binding.ExecutableRelative),folder=binding.FolderRelative==null?null:Resolve(current.Root,binding.FolderRelative);
            NoLinks(current.Root,executable);
            var target=new AppTarget{Name=app.Name,Executable=executable,Folder=folder,AppId=app.AppId,Package=Clone(binding)};target.Package.FullName=current.FullName;
            // A same-version hash change is not an update and must remain an alert.
            target.Hash=Profile.EqualPath(executable,app.Executable)?app.Hash:Profile.FileHash(executable);
            if(hashes.Read(executable)!=target.Hash)throw new InvalidDataException("软件文件改变但应用包版本没变，拒绝自动接受。");
            string oldRoot=ExistingRoot(app.Executable);
            foreach(var service in app.Services){
                string relative=service.PackageRelative??InPackage(service.Executable,oldRoot),actual=servicePath(service.Name);
                var owner=packages.SingleOrDefault(x=>Profile.EqualPath(Resolve(x.Root,relative),actual));
                if(owner==null || folder==null || !Profile.Within(actual,folder))throw new InvalidDataException("专属服务未切换到已验证的新目录，需复核："+service.Name);
                NoLinks(owner.Root,actual);string hash=Profile.EqualPath(actual,service.Executable)?service.Hash:Profile.FileHash(actual);
                if(hashes.Read(actual)!=hash)throw new InvalidDataException("服务文件改变但版本没变，拒绝自动接受："+service.Name);
                target.Services.Add(new ServiceTarget{Name=service.Name,Executable=actual,Hash=hash,PackageRelative=relative});
            }
            return target;
        }
        static bool SameTarget(AppTarget a,AppTarget b)
        {
            var x=a.Package;var y=b.Package;
            if(a.Name!=b.Name || !Profile.EqualPath(a.Executable,b.Executable) || a.Hash!=b.Hash || a.AppId!=b.AppId || !((a.Folder==null&&b.Folder==null)||Profile.EqualPath(a.Folder,b.Folder)) || x==null || y==null)return false;
            if(x.Family!=y.Family || x.Publisher!=y.Publisher || x.FullName!=y.FullName || x.ExecutableRelative!=y.ExecutableRelative || x.FolderRelative!=y.FolderRelative || a.Services.Count!=b.Services.Count)return false;
            for(int i=0;i<a.Services.Count;i++){var old=a.Services[i];var next=b.Services[i];if(old.Name!=next.Name || !Profile.EqualPath(old.Executable,next.Executable) || old.Hash!=next.Hash || old.PackageRelative!=next.PackageRelative)return false;}
            return true;
        }
        void ObserveVersions(AppTarget app,PackageBinding binding,List<RegisteredPackage> packages)
        {
            foreach(var package in packages){
                if(!package.Trusted || package.Family!=binding.Family || package.Publisher!=binding.Publisher || !package.Executables.Contains(binding.ExecutableRelative,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("应用更新身份无法确认。");
                string key=package.FullName+"/"+binding.ExecutableRelative+"/"+(binding.FolderRelative??"");if(versions.ContainsKey(key))continue;
                string exe=Resolve(package.Root,binding.ExecutableRelative);NoLinks(package.Root,exe);
                string hash=Profile.EqualPath(exe,app.Executable)?app.Hash:Profile.FileHash(exe);
                if(hashes.Read(exe)!=hash)throw new InvalidDataException("文件改变但版本没变，拒绝扩大进程范围。");
                var target=new AppTarget{Name=app.Name,Executable=exe,Hash=hash,Folder=binding.FolderRelative==null?null:Resolve(package.Root,binding.FolderRelative)};
                if(audit!=null && !audit.Write("app_package_version_observed",new{app=app.Name,family=binding.Family,package=package.FullName,path=exe}))throw new IOException("版本识别日志无法保存。");
                versions.Add(key,target);
            }
        }
        public List<string> Refresh(bool force=false)
        {
            if(!force && DateTime.UtcNow<next)return new List<string>(last);next=DateTime.UtcNow.AddSeconds(3);last=new List<string>();
            for(int i=0;i<profile.Apps.Count;i++){
                var app=profile.Apps[i];try{
                    var binding=app.Package??Legacy(app);if(binding==null)continue;
                    var packages=source.Find(binding.Family);ObserveVersions(app,binding,packages);
                    var target=Plan(app,binding,packages);if(SameTarget(app,target))continue;
                    // Persist and log BEFORE applying new kill authority. Environment
                    // settings, captured time, exit, listener and selected service
                    // names remain byte-for-byte semantically unchanged.
                    var record=new{app=app.Name,family=binding.Family,from=app.Executable,to=target.Executable,services=target.Services.Select(x=>new{name=x.Name,path=x.Executable}).ToArray(),migration=app.Package==null};
                    if(audit!=null && !audit.Write("app_update_binding_prepared",record))throw new IOException("接续日志无法保存。");
                    if(profileFile!=null){var copy=new JavaScriptSerializer().Deserialize<Profile>(new JavaScriptSerializer().Serialize(profile));copy.Apps[i]=target;copy.Save(profileFile);}
                    if(!retired.Any(x=>Profile.EqualPath(x.Executable,app.Executable)&&x.Hash==app.Hash&&x.Folder==app.Folder))retired.Add(app);profile.Apps[i]=target;
                    if(audit!=null && !audit.Write("app_update_rebound",record))last.Add(app.Name+" 已接续，但结果日志写入失败。");
                }catch(Exception ex){last.Add(app.Name+" 自动接续未确认："+ex.Message);}
            }
            return new List<string>(last);
        }
        internal IEnumerable<AppTarget> Coverage {get{return versions.Values.Concat(retired);}}
    }
}
