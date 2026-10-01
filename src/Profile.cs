using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace EnvGuard
{
    public sealed class AppTarget
    {
        public string Name { get; set; }
        public string Executable { get; set; }
        public string Hash { get; set; }
        public string Folder { get; set; }
        public string AppId { get; set; }
        public List<ServiceTarget> Services { get; set; }
        public AppTarget() { Services = new List<ServiceTarget>(); }
        public override string ToString() { return Name + "  —  " + Executable; }
    }
    public sealed class ServiceTarget
    {
        public string Name { get; set; }
        public string Executable { get; set; }
        public string Hash { get; set; }
        public override string ToString() { return Name + "  —  " + Executable; }
    }
    public sealed class Profile
    {
        public int Schema { get; set; }
        public string CapturedAt { get; set; }
        public string UserSid { get; set; }
        public string Computer { get; set; }
        public string LogFile { get; set; }
        public string ProxyHost { get; set; }
        public int ProxyPort { get; set; }
        public string ExitIp { get; set; }
        public string ProxyExecutable { get; set; }
        public string ProxyHash { get; set; }
        public Dictionary<string, string> Settings { get; set; }
        public string BrowserPreferences { get; set; }
        public string V2rayConfig { get; set; }
        public List<AppTarget> Apps { get; set; }
        public int NetworkTimeoutMs { get; set; }
        public string GitHubRepository { get; set; }
        public Profile()
        {
            Schema = 1; Apps = new List<AppTarget>(); Settings = new Dictionary<string, string>();
            ProxyHost = "127.0.0.1"; ProxyPort = 10808; NetworkTimeoutMs = 2500;
        }
        public void Validate()
        {
            System.Net.IPAddress proxy, exit;
            if (Schema != 1 || String.IsNullOrWhiteSpace(UserSid) || String.IsNullOrWhiteSpace(Computer)) throw new InvalidDataException("配置版本或设备身份无效，请在本设备重新配置。");
            if (!System.Net.IPAddress.TryParse(ProxyHost, out proxy) || !System.Net.IPAddress.IsLoopback(proxy) || proxy.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || ProxyPort < 1 || ProxyPort > 65535) throw new InvalidDataException("需要 IPv4 本机 HTTP／mixed 代理，端口为 1—65535。");
            if (!System.Net.IPAddress.TryParse(ExitIp, out exit) || System.Net.IPAddress.IsLoopback(exit)) throw new InvalidDataException("预期出口 IP 无效。");
            if (NetworkTimeoutMs < 500 || NetworkTimeoutMs > 10000) throw new InvalidDataException("网络检测时限无效。");
            if (String.IsNullOrWhiteSpace(ProxyExecutable) || !Path.IsPathRooted(ProxyExecutable) || !ValidHash(ProxyHash)) throw new InvalidDataException("代理监听程序身份不完整。");
            if (!String.IsNullOrEmpty(GitHubRepository) && !System.Text.RegularExpressions.Regex.IsMatch(GitHubRepository, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z")) throw new InvalidDataException("GitHub 仓库格式应为账号/仓库。");
            if (!LocalLogPath(LogFile)) throw new InvalidDataException("请选择本机磁盘的绝对日志路径（.jsonl）；不支持网络盘或共享路径，避免断网拖住紧急关闭。");
            if (Apps == null || Apps.Count == 0 || Apps.Count > 20) throw new InvalidDataException("请选择 1—20 个保护软件。");
            if (Settings == null || Settings.Count < 4 || Settings.Count > 100) throw new InvalidDataException("环境快照不完整。");
            foreach (AppTarget app in Apps)
            {
                if (app == null || String.IsNullOrWhiteSpace(app.Name) || EqualPath(app.Executable, System.Reflection.Assembly.GetExecutingAssembly().Location)) throw new InvalidDataException("不能选择预警程序自身。");
                if (!Path.IsPathRooted(app.Executable) || !app.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || ProcessScope.IsSystemPath(app.Executable)) throw new InvalidDataException("不能保护系统组件，或软件路径无效。");
                if (!ValidHash(app.Hash)) throw new InvalidDataException("软件身份校验值无效。");
                if (!String.IsNullOrEmpty(app.Folder) && (!SafeFolder(app.Folder) || !Within(app.Executable, app.Folder))) throw new InvalidDataException("不能把系统目录、用户目录或共享目录作为软件清理范围。");
                if (app.Services == null) app.Services = new List<ServiceTarget>();
                if (app.Services.Count > 32) throw new InvalidDataException("每个软件最多选择 32 个专属服务。");
                foreach (ServiceTarget service in app.Services)
                    if (String.IsNullOrWhiteSpace(service.Name) || !Path.IsPathRooted(service.Executable) || ProcessScope.IsSystemPath(service.Executable) || !ValidHash(service.Hash) || !Within(service.Executable, app.Folder)) throw new InvalidDataException("后台服务必须属于选定的软件安装目录。");
            }
        }
        public static bool ValidHash(string value) { return value != null && value.Length == 64 && value.All(Uri.IsHexDigit); }
        public static bool LocalLogPath(string path)
        {
            if(String.IsNullOrWhiteSpace(path) || !System.Text.RegularExpressions.Regex.IsMatch(path,@"\A[A-Za-z]:[\\/].+\.jsonl\z",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return false;
            try{var type=new DriveInfo(Path.GetPathRoot(path)).DriveType;return type==DriveType.Fixed || type==DriveType.Removable || type==DriveType.Ram;}catch{return false;}
        }
        public static bool EqualPath(string a, string b)
        { return !String.IsNullOrEmpty(a) && !String.IsNullOrEmpty(b) && String.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        public static bool Within(string path, string folder)
        { return !String.IsNullOrWhiteSpace(path) && !String.IsNullOrWhiteSpace(folder) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
        public static bool SafeFolder(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) return false;
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] broad = {Path.GetPathRoot(full), user, Path.Combine(user,"Downloads"), Path.Combine(user,"Documents"), Path.Combine(user,"Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.GetTempPath()};
            return !EqualPath(full, windows) && !Within(full, windows) && !broad.Any(x => EqualPath(full,x));
        }
        public static string FileHash(string path)
        { using (SHA256 hash = SHA256.Create()) using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", ""); }
        public static Profile Load(string path)
        {
            if (new FileInfo(path).Length > 1024*1024) throw new InvalidDataException("配置文件过大。");
            Profile p = new JavaScriptSerializer().Deserialize<Profile>(File.ReadAllText(path));
            if (p == null) throw new InvalidDataException("配置为空。"); p.Validate(); return p;
        }
        public void Save(string path)
        {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".pending";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp,path);
        }
    }
    public sealed class AuditLog
    {
        readonly string path;
        readonly string session = Guid.NewGuid().ToString("N");
        readonly object gate = new object();
        long sequence;
        public string Error { get; private set; }
        public AuditLog(string file) { path = Path.GetFullPath(file); }
        public bool Write(string kind, object data)
        {
            lock(gate) try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var now = DateTimeOffset.Now;
                string line = new JavaScriptSerializer().Serialize(new { schema=1, session=session, sequence=++sequence, time=now.ToString("o"), utc=now.UtcDateTime.ToString("o"), kind=kind, data=data });
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line + "\r\n");
                for(int i=0;;i++) try
                {
                    using(var file = new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough)){file.Write(bytes,0,bytes.Length);file.Flush(true);} break;
                }catch(IOException){if(i>=4)throw;System.Threading.Thread.Sleep(25);}
                Error=null; return true;
            }catch(Exception ex){Error=ex.Message;return false;}
        }
    }
}
