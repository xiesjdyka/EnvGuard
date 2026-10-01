using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace EnvGuard
{
    // Cache large binaries by file metadata; re-hash at least every 10 seconds.
    public sealed class HashCache
    {
        sealed class Entry {public long Size,Stamp;public DateTime Checked;public string Hash;}
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        public string Read(string path)
        {
            var f=new FileInfo(path);Entry e;
            if(entries.TryGetValue(path,out e) && f.Exists && e.Size==f.Length && e.Stamp==f.LastWriteTimeUtc.Ticks && DateTime.UtcNow-e.Checked<TimeSpan.FromSeconds(10))return e.Hash;
            long size=f.Length,stamp=f.LastWriteTimeUtc.Ticks;string hash=Profile.FileHash(path);f.Refresh();
            if(size!=f.Length || stamp!=f.LastWriteTimeUtc.Ticks)throw new IOException("校验期间文件改变。");
            entries[path]=new Entry{Size=size,Stamp=stamp,Hash=hash,Checked=DateTime.UtcNow};return hash;
        }
    }
    public sealed class NetworkResult
    {
        public List<string> Confirmed=new List<string>(),Unconfirmed=new List<string>();
        public string[] Addresses;
        public bool Healthy {get{return Confirmed.Count==0 && Unconfirmed.Count==0 && Addresses!=null && Addresses.Length==2;}}
    }
    public sealed class EnvironmentChecker
    {
        readonly Profile profile;
        readonly HashCache hashes=new HashCache();
        public EnvironmentChecker(Profile p){profile=p;}
        public static string Sid(){using(var i=WindowsIdentity.GetCurrent())return i.User.Value;}
        internal static string Value(RegistryHive hive,string path,string name)
        {
            using(var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64))using(var k=root.OpenSubKey(path)){
                if(k==null)return "<未设置>";
                if(!k.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase)){
                    if(!k.GetSubKeyNames().Contains(name,StringComparer.OrdinalIgnoreCase))return "<未设置>";
                    using(var sub=k.OpenSubKey(name)){string tree=new JavaScriptSerializer().Serialize(RegistryTree(sub,0));if(tree.Length>65536)throw new InvalidDataException("策略子键过大。");return "子键:"+tree;}
                }
                object v=k.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(v is byte[])return k.GetValueKind(name)+":"+Convert.ToBase64String((byte[])v);
                if(v is string[])return k.GetValueKind(name)+":"+String.Join(",",(string[])v);
                return k.GetValueKind(name)+":"+Convert.ToString(v,CultureInfo.InvariantCulture);
            }
        }
        static SortedDictionary<string,object> RegistryTree(RegistryKey key,int depth)
        {
            if(key==null || depth>3)throw new InvalidDataException("策略子键不可读取或过深。");var result=new SortedDictionary<string,object>(StringComparer.Ordinal);
            string[] values=key.GetValueNames(),children=key.GetSubKeyNames();if(values.Length+children.Length>64)throw new InvalidDataException("策略子键条目过多。");
            foreach(string name in values){object value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);string text=value is byte[]?Convert.ToBase64String((byte[])value):value is string[]?String.Join(",",(string[])value):Convert.ToString(value,CultureInfo.InvariantCulture);if(text.Length>16384)throw new InvalidDataException("策略值过大。");result["value/"+name]=key.GetValueKind(name)+":"+text;}
            foreach(string name in children)using(var child=key.OpenSubKey(name))result["key/"+name]=RegistryTree(child,depth+1);return result;
        }
        static Dictionary<string,object> ReadJson(string file)
        {
            var before=new FileInfo(file);if(!before.Exists || before.Length>32*1024*1024)throw new IOException("配置文件不存在或过大。");
            long stamp=before.LastWriteTimeUtc.Ticks,size=before.Length;string text;
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))text=reader.ReadToEnd();
            before.Refresh();if(before.Length!=size || before.LastWriteTimeUtc.Ticks!=stamp)throw new IOException("配置文件正在写入。");
            var root=new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.DeserializeObject(text) as Dictionary<string,object>;
            if(root==null)throw new InvalidDataException("配置文件格式不兼容。");return root;
        }
        static object Field(Dictionary<string,object> root,string name){object value;if(!root.TryGetValue(name,out value))throw new InvalidDataException("缺少配置字段 "+name);return value;}
        public static Dictionary<string,string> Snapshot(Profile p)
        {
            var values=new Dictionary<string,string>();TimeZoneInfo.ClearCachedData();var tz=TimeZoneInfo.Local;
            values["设备"]=Environment.MachineName;values["用户身份"]=Sid();values["Windows 时区"]=tz.Id;
            values["夏令时开关"]=Value(RegistryHive.LocalMachine,@"SYSTEM\CurrentControlSet\Control\TimeZoneInformation","DynamicDaylightTimeDisabled");
            values["时区规则"]=tz.BaseUtcOffset.Ticks+":"+new JavaScriptSerializer().Serialize(tz.GetAdjustmentRules().Select(r=>new {from=r.DateStart,to=r.DateEnd,delta=r.DaylightDelta.Ticks,start=r.DaylightTransitionStart,end=r.DaylightTransitionEnd}).ToArray());
            foreach(string name in new[]{"LocaleName","sShortDate","sLongDate","sTimeFormat","sDecimal","sThousand"})values["区域/"+name]=Value(RegistryHive.CurrentUser,@"Control Panel\International",name);
            foreach(string name in new[]{"ProxyEnable","ProxyServer","ProxyOverride","AutoConfigURL","AutoDetect"})values["系统代理/"+name]=Value(RegistryHive.CurrentUser,@"Software\Microsoft\Windows\CurrentVersion\Internet Settings",name);
            // Ignore WinINet's changing revision counter; only compare connection-mode flags.
            using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections")){
                var bytes=k==null?null:k.GetValue("DefaultConnectionSettings") as byte[];
                values["系统代理/连接模式"]=bytes==null?"<未设置>":bytes.Length<12?"<格式异常>":BitConverter.ToInt32(bytes,8).ToString(CultureInfo.InvariantCulture);
            }
            if(!String.IsNullOrWhiteSpace(p.BrowserPreferences)){
                var root=ReadJson(p.BrowserPreferences);var intl=Field(root,"intl") as Dictionary<string,object>;if(intl==null)throw new InvalidDataException("浏览器语言配置缺失。");
                values["Chrome/accept_languages"]=Convert.ToString(Field(intl,"accept_languages"));object selected;values["Chrome/selected_languages"]=intl.TryGetValue("selected_languages",out selected)?Convert.ToString(selected):"<未设置>";
                foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(string name in new[]{"WebRtcIPHandling","NetworkPredictionOptions","QuicAllowed","ProxySettings","BackgroundModeEnabled","SyncDisabled","WebRtcIPHandlingUrl","ProxyOverrideRules","RoamingProfileSupportEnabled","ProxyMode","ProxyServer","ProxyBypassList","ProxyPacUrl"})values["Chrome/"+hive+"/"+name]=Value(hive,@"Software\Policies\Google\Chrome",name);
            }
            if(!String.IsNullOrWhiteSpace(p.V2rayConfig)){
                var root=ReadJson(p.V2rayConfig);values["v2rayN/已保存节点"]=Convert.ToString(Field(root,"IndexId"));
                var routing=Field(root,"RoutingBasicItem") as Dictionary<string,object>;var tun=Field(root,"TunModeItem") as Dictionary<string,object>;
                if(routing==null || tun==null)throw new InvalidDataException("v2rayN 配置格式不兼容。");
                values["v2rayN/已保存路由"]=Convert.ToString(Field(routing,"RoutingIndexId"));var enabled=Field(tun,"EnableTun");if(!(enabled is bool))throw new InvalidDataException("TUN 配置类型错误。");values["v2rayN/已保存TUN"]=enabled.ToString();
            }
            return values;
        }
        public List<string> Local()
        {
            var errors=new List<string>();try{var current=Snapshot(profile);foreach(var pair in profile.Settings){string value;if(!current.TryGetValue(pair.Key,out value) || value!=pair.Value)errors.Add(pair.Key+" 与确认的基准不同。");}}catch(Exception ex){errors.Add("本地环境无法确认："+ex.Message);}
            try{
                int pid=Native.ListenerPid(profile.ProxyPort);if(pid==0)errors.Add("本机代理端口没有监听。");else{
                    var p=Native.Read(pid,0,"");try{if(p==null || !Profile.EqualPath(p.Path,profile.ProxyExecutable))errors.Add("代理监听程序与基准不同。");else if(hashes.Read(p.Path)!=profile.ProxyHash)errors.Add("代理程序已更新或改变。");}finally{if(p!=null)p.Dispose();}
                }
            }catch(Exception ex){errors.Add("代理监听身份无法确认："+ex.Message);}return errors;
        }
        internal static HttpClientHandler Handler(Profile p)
        {
            return new HttpClientHandler{UseProxy=true,Proxy=new WebProxy("http://"+p.ProxyHost+":"+p.ProxyPort){BypassProxyOnLocal=false},AllowAutoRedirect=false,UseCookies=false};
        }
        public static HttpClient Client(Profile p)
        {
            var handler=Handler(p);
            var client=new HttpClient(handler){Timeout=TimeSpan.FromMilliseconds(p.NetworkTimeoutMs)};client.DefaultRequestHeaders.UserAgent.ParseAdd("EnvGuard/"+Program.Version);return client;
        }
        internal static async Task<string> Ip(HttpClient client,string url,CancellationToken token)
        {
            using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseContentRead,token).ConfigureAwait(false)){
                response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>128)throw new InvalidDataException("出口检测响应过大。");
                string text=(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();IPAddress ip;if(text.Length>64 || !IPAddress.TryParse(text,out ip) || IPAddress.IsLoopback(ip))throw new InvalidDataException("出口检测未返回有效 IP。");return ip.ToString();
            }
        }
        internal static NetworkResult Classify(string expected,string[] ips,string[] failures)
        {
            var r=new NetworkResult();r.Addresses=ips;
            for(int i=0;i<ips.Length;i++){if(ips[i]!=null && ips[i]!=expected && !String.IsNullOrEmpty(expected))r.Confirmed.Add("出口变化：预期 "+expected+"，检测到 "+ips[i]+"。");if(failures[i]!=null)r.Unconfirmed.Add(failures[i]);}
            if(ips.All(x=>x!=null) && ips[0]!=ips[1])r.Confirmed.Add("两个独立出口检测结果不一致。");return r;
        }
        public async Task<NetworkResult> Network(CancellationToken token)
        {
            using(var client=Client(profile))return await NetworkUsing(client,token).ConfigureAwait(false);
        }
        internal async Task<NetworkResult> NetworkUsing(HttpClient client,CancellationToken token)
        {
                string[] urls={"https://api.ipify.org","https://checkip.amazonaws.com"};string[] ips=new string[2],failures=new string[2];
                await Task.WhenAll(urls.Select(async(url,i)=>{try{ips[i]=await Ip(client,url,token).ConfigureAwait(false);}catch(Exception ex){if(token.IsCancellationRequested)throw;failures[i]="出口检查 "+(i+1)+" 尚未确认（"+ex.GetType().Name+"），不等于已确定代理掉线。";}})).ConfigureAwait(false);
                return Classify(profile.ExitIp,ips,failures);
        }
        public static async Task Capture(Profile p)
        {
            p.UserSid=Sid();p.Computer=Environment.MachineName;p.CapturedAt=DateTimeOffset.Now.ToString("o");p.Settings=Snapshot(p);
            int pid=Native.ListenerPid(p.ProxyPort);if(pid==0)throw new InvalidOperationException("所选本机代理端口没有监听，不能保存基准。");
            var owner=Native.Read(pid,0,"");try{if(owner==null || owner.Path==null)throw new IOException("代理程序身份无法读取。");p.ProxyExecutable=owner.Path;p.ProxyHash=Profile.FileHash(owner.Path);}finally{if(owner!=null)owner.Dispose();}
            var result=await new EnvironmentChecker(p).Network(CancellationToken.None).ConfigureAwait(false);
            if(!result.Healthy || result.Addresses[0]!=result.Addresses[1])throw new InvalidOperationException(String.Join("\r\n",result.Confirmed.Concat(result.Unconfirmed)) + "\r\n出口未一致确认，不能保存基准。");
            p.ExitIp=result.Addresses[0];p.Settings=Snapshot(p);p.Validate();
        }
    }
}
