using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace EnvGuard
{
    internal static class UI
    {
        public static readonly Color Ink=Color.FromArgb(30,43,60),Blue=Color.FromArgb(24,78,139),Red=Color.FromArgb(167,28,35),Green=Color.FromArgb(28,108,65);
        public static void Base(Form form,string title,int width,int height){form.Text=title;form.Font=new Font("Microsoft YaHei UI",10);form.BackColor=Color.White;form.ForeColor=Ink;form.StartPosition=FormStartPosition.CenterScreen;form.ClientSize=new Size(width,height);form.MinimumSize=new Size(width+16,height+39);form.AutoScaleMode=AutoScaleMode.Dpi;form.Icon=SystemIcons.Shield;}
        public static Label Label(string text,int height){return new Label{Text=text,AutoSize=false,Height=height,Dock=DockStyle.Top,TextAlign=ContentAlignment.MiddleLeft};}
        public static TextBox Text(string text){return new TextBox{Text=text,Dock=DockStyle.Fill};}
        public static Button Button(string text,EventHandler click){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(112,40),Margin=new Padding(0,0,10,0),FlatStyle=FlatStyle.System};if(click!=null)b.Click+=click;return b;}
        public static TextBox Details(){return new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,BackColor=Color.FromArgb(246,248,251),BorderStyle=BorderStyle.FixedSingle};}
        public static FlowLayoutPanel Buttons(){return new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=54,Padding=new Padding(0,8,0,0),WrapContents=false};}
        public static void Error(string message){MessageBox.Show(message,"EnvGuard",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        public static void Browse(TextBox target,string title,string filter){using(var d=new OpenFileDialog{Title=title,Filter=filter})if(d.ShowDialog()==DialogResult.OK)target.Text=d.FileName;}
        public static void Field(TableLayoutPanel grid,int row,string label,Control control,Button browse){grid.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,row);grid.Controls.Add(control,1,row);if(browse!=null)grid.Controls.Add(browse,2,row);}
        public static TableLayoutPanel Grid(int rows){var g=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,RowCount=rows,Padding=new Padding(4)};g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,144));g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));for(int i=0;i<rows;i++)g.RowStyles.Add(new RowStyle(SizeType.Absolute,50));return g;}
    }
    public sealed class TargetChoice
    {
        public string Name,Path,AppId;
        public override string ToString(){return Name+"  —  "+Path;}
    }
    public sealed class PickerForm : Form
    {
        readonly ListBox list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};public TargetChoice Selected;
        public PickerForm(bool installed)
        {
            UI.Base(this,installed?"选择已安装的软件包":"选择正在运行的软件",860,480);Padding=new Padding(20);
            var buttons=UI.Buttons();buttons.Controls.Add(UI.Button("选择",(s,e)=>{Selected=list.SelectedItem as TargetChoice;if(Selected!=null){DialogResult=DialogResult.OK;Close();}}));buttons.Controls.Add(UI.Button("取消",(s,e)=>Close()));Controls.Add(list);Controls.Add(buttons);
            Shown+=async(s,e)=>{list.Items.Add("正在读取…");try{var choices=await Task.Run(()=>Choices(installed));list.Items.Clear();list.Items.AddRange(choices.Cast<object>().ToArray());if(choices.Count==0)list.Items.Add("没有找到可选择的软件；请使用“选择 EXE”。");}catch(Exception ex){list.Items.Clear();UI.Error(ex.Message);}};
        }
        static List<TargetChoice> Choices(bool installed)
        {
            var result=new List<TargetChoice>();if(!installed){var records=Native.Snapshot();try{foreach(var p in records)if(p.Path!=null && !ProcessScope.IsSystemPath(p.Path))result.Add(new TargetChoice{Name=p.Name,Path=p.Path});}finally{foreach(var p in records)p.Dispose();}}
            else using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")){
                if(key!=null)foreach(var name in key.GetSubKeyNames())try{using(var k=key.OpenSubKey(name)){
                    string folder=Convert.ToString(k.GetValue("PackageRootFolder"));if(String.IsNullOrEmpty(folder))continue;
                    var doc=new XmlDocument{XmlResolver=null};doc.Load(Path.Combine(folder,"AppxManifest.xml"));
                    foreach(XmlElement a in doc.SelectNodes("//*[local-name()='Applications']/*[local-name()='Application']")){
                        string exe=a.GetAttribute("Executable"),id=a.GetAttribute("Id");string path=Path.Combine(folder,exe.Replace('/', '\\'));
                        if(exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) && File.Exists(path))result.Add(new TargetChoice{Name=name.Split('_')[0]+" / "+id,Path=path});
                    }
                }}catch{}
            }
            return result.GroupBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(x=>x.Name).ToList();
        }
    }
    public sealed class ScopeForm : Form
    {
        readonly CheckBox includeFolder=new CheckBox{Text="确认下列目录专属于这个软件，包含其后台组件",AutoSize=true};
        readonly CheckedListBox services=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
        readonly TextBox folder=UI.Text("");readonly AppTarget target;
        public ScopeForm(AppTarget app)
        {
            target=app;UI.Base(this,"确认紧急关闭范围",850,470);Padding=new Padding(20);
            folder.Text=Path.GetDirectoryName(app.Executable);folder.ReadOnly=true;folder.Height=30;folder.Dock=DockStyle.Top;includeFolder.Dock=DockStyle.Top;includeFolder.Height=36;includeFolder.Checked=false;
            var explanation=UI.Label("仅选 EXE：主进程 + 运行期间观察到的可验证子进程。\r\n确认专属目录后：还包括该目录内的后台进程。服务需单独勾选。",65);
            var notice=UI.Label("共享系统服务、其他软件和未知虚拟机不会被批量结束。软件升级后要重新确认。",50);
            var buttons=UI.Buttons();buttons.Controls.Add(UI.Button("确认范围",(s,e)=>{if(includeFolder.Checked && !Profile.SafeFolder(folder.Text)){UI.Error("该目录过于宽泛，不能用作清理范围。");return;}target.Folder=includeFolder.Checked?folder.Text:null;target.Services=includeFolder.Checked?services.CheckedItems.Cast<ServiceTarget>().ToList():new List<ServiceTarget>();DialogResult=DialogResult.OK;Close();}));buttons.Controls.Add(UI.Button("取消",(s,e)=>Close()));
            Controls.Add(services);Controls.Add(notice);Controls.Add(folder);Controls.Add(includeFolder);Controls.Add(explanation);Controls.Add(buttons);
            Shown+=async(s,e)=>{try{var found=await Task.Run(()=>Services.Discover(folder.Text));foreach(var item in found)services.Items.Add(item,false);if(found.Count>0)notice.Text="找到 "+found.Count+" 个目录内服务；需先确认专属目录，再勾选要一同结束的服务。未选服务不在关闭范围。";}catch(Exception ex){notice.Text="专属服务读取失败："+ex.Message;} };
        }
    }
    public sealed class SetupForm : Form
    {
        readonly string profileFile;readonly ListBox apps=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        readonly TextBox port=UI.Text("10808"),expected=UI.Text(""),logFile=UI.Text(""),browser=UI.Text(""),v2ray=UI.Text(""),repository=UI.Text("");
        readonly TextBox review=UI.Details();readonly CheckBox confirm=new CheckBox{Text="我已核对出口 IP、环境和软件范围，确认这是正确基准",AutoSize=true};
        readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};readonly Button save,capture;readonly Label status=UI.Label("尚未生成基准；程序不会替你判断当前代理是否可靠。",36);
        Profile draft;DateTime captured;bool busy;public Profile Result;
        public SetupForm(string file,Profile previous)
        {
            profileFile=file;UI.Base(this,"EnvGuard · 首次配置 / 重新确认",920,700);Padding=new Padding(22);
            var heading=UI.Label("确认环境，再开始监测",45);heading.Font=new Font(Font.FontFamily,18,FontStyle.Bold);
            var subtitle=UI.Label("此配置只属于当前电脑和用户；不会锁死日期、当前时间或夏令时偏移。",38);
            var software=new TabPage("1  保护软件"){Padding=new Padding(12)};var environment=new TabPage("2  环境与日志"){Padding=new Padding(12),AutoScroll=true};var baseline=new TabPage("3  核对基准"){Padding=new Padding(12)};
            tabs.TabPages.AddRange(new[]{software,environment,baseline});
            var appButtons=UI.Buttons();appButtons.Controls.Add(UI.Button("选择 EXE",async(s,e)=>{using(var d=new OpenFileDialog{Filter="软件 (*.exe)|*.exe"})if(d.ShowDialog()==DialogResult.OK)await Add(new TargetChoice{Name=Path.GetFileNameWithoutExtension(d.FileName),Path=d.FileName});}));
            appButtons.Controls.Add(UI.Button("正在运行的软件",async(s,e)=>{using(var d=new PickerForm(false))if(d.ShowDialog(this)==DialogResult.OK)await Add(d.Selected);}));appButtons.Controls.Add(UI.Button("已安装的软件包",async(s,e)=>{using(var d=new PickerForm(true))if(d.ShowDialog(this)==DialogResult.OK)await Add(d.Selected);}));
            appButtons.Controls.Add(UI.Button("移除",(s,e)=>{if(apps.SelectedItem!=null){apps.Items.Remove(apps.SelectedItem);InvalidateDraft();}}));
            software.Controls.Add(apps);software.Controls.Add(UI.Label("选定的软件不会自动启动或自动关闭。可添加多个；先关闭不需要保护的软件再选择。",50));software.Controls.Add(appButtons);
            var grid=UI.Grid(6);
            UI.Field(grid,0,"本机代理端口",port,null);UI.Field(grid,1,"预期 IP（可留空）",expected,null);
            UI.Field(grid,2,"日志文件",logFile,UI.Button("选择文件",(s,e)=>{using(var d=new SaveFileDialog{Filter="追加日志 (*.jsonl)|*.jsonl",FileName="events.jsonl",OverwritePrompt=false})if(d.ShowDialog()==DialogResult.OK)logFile.Text=d.FileName;}));
            UI.Field(grid,3,"Chrome 配置（可选）",browser,UI.Button("浏览",(s,e)=>UI.Browse(browser,"选择所用 Chrome 配置目录的 Preferences","Preferences|Preferences|所有文件|*.*")));
            UI.Field(grid,4,"v2rayN 配置（可选）",v2ray,UI.Button("浏览",(s,e)=>UI.Browse(v2ray,"选择 guiNConfig.json","v2rayN 设置|guiNConfig.json|JSON|*.json")));
            UI.Field(grid,5,"更新仓库（可选）",repository,null);
            environment.Controls.Add(grid);environment.Controls.Add(UI.Label("仓库填写“账号/仓库”。网络检查始终走上述本机代理；浏览器、v2rayN 只检查选定文件的已保存配置。\r\n环境快照不是防泄漏隔离，也不能保证软件的每一条连接都走代理。",90));
            confirm.Dock=DockStyle.Bottom;confirm.Height=42;baseline.Controls.Add(review);baseline.Controls.Add(confirm);
            var buttons=UI.Buttons();capture=UI.Button("检测并生成快照",async(s,e)=>await CaptureBaseline());save=UI.Button("保存并开始监测",async(s,e)=>await Save());save.Enabled=false;buttons.Controls.Add(capture);buttons.Controls.Add(save);buttons.Controls.Add(UI.Button("取消",(s,e)=>Close()));
            Controls.Add(tabs);Controls.Add(subtitle);Controls.Add(heading);Controls.Add(status);Controls.Add(buttons);
            logFile.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EnvGuard","logs","events.jsonl");
            repository.Text=Program.DefaultRepository;
            if(previous!=null){foreach(var a in previous.Apps)apps.Items.Add(a);port.Text=previous.ProxyPort.ToString();expected.Text=previous.ExitIp;logFile.Text=previous.LogFile;browser.Text=previous.BrowserPreferences;v2ray.Text=previous.V2rayConfig;repository.Text=previous.GitHubRepository;}
            foreach(var box in new[]{port,expected,logFile,browser,v2ray,repository})box.TextChanged+=(s,e)=>InvalidateDraft();confirm.CheckedChanged+=(s,e)=>save.Enabled=confirm.Checked && draft!=null && !busy;
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="正在检测，请等当前操作完成再关闭。";}};
        }
        void InvalidateDraft(){draft=null;confirm.Checked=false;save.Enabled=false;review.Clear();}
        async Task Add(TargetChoice choice)
        {
            if(choice==null)return;try{
                if(ProcessScope.IsSystemPath(choice.Path) || Profile.EqualPath(choice.Path,System.Reflection.Assembly.GetExecutingAssembly().Location))throw new InvalidOperationException("不能选择系统组件或 EnvGuard 本身。");
                if(apps.Items.Cast<AppTarget>().Any(x=>Profile.EqualPath(x.Executable,choice.Path)))return;
                status.Text="正在读取软件身份…";var target=new AppTarget{Name=choice.Name,Executable=Path.GetFullPath(choice.Path),AppId=choice.AppId,Hash=await Task.Run(()=>Profile.FileHash(choice.Path))};
                using(var d=new ScopeForm(target))if(d.ShowDialog(this)==DialogResult.OK){apps.Items.Add(target);InvalidateDraft();}status.Text="已添加软件；请确认环境后生成快照。";
            }catch(Exception ex){UI.Error(ex.Message);}
        }
        Profile Input()
        {
            int n;if(!Int32.TryParse(port.Text,out n) || n<1 || n>65535)throw new InvalidOperationException("代理端口应为 1—65535。");if(apps.Items.Count==0)throw new InvalidOperationException("先选择需要保护的软件。");
            string file=Path.GetFullPath(logFile.Text);if(!file.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("日志文件需要 .jsonl 扩展名。");
            return new Profile{ProxyPort=n,ExitIp=expected.Text.Trim(),LogFile=file,Apps=apps.Items.Cast<AppTarget>().ToList(),BrowserPreferences=browser.Text.Trim(),V2rayConfig=v2ray.Text.Trim(),GitHubRepository=repository.Text.Trim()};
        }
        void Busy(bool value){busy=value;capture.Enabled=!value;tabs.Enabled=!value;save.Enabled=!value && confirm.Checked && draft!=null;}
        async Task CaptureBaseline()
        {
            Busy(true);status.Text="正在核对代理身份与两个独立出口…";try{var p=Input();await Task.Run(async()=>{foreach(var app in p.Apps){app.Hash=Profile.FileHash(app.Executable);foreach(var service in app.Services)Services.Verify(service);}await EnvironmentChecker.Capture(p);});
                draft=p;captured=DateTime.UtcNow;review.Text=Describe(p);tabs.SelectedIndex=2;status.Text="快照已生成；请核对显示的出口 IP，不会自动接受为正确环境。";confirm.Checked=false;
            }catch(Exception ex){draft=null;status.Text="不能保存基准："+ex.Message;UI.Error(ex.Message);}finally{Busy(false);}
        }
        async Task Save()
        {
            if(draft==null || !confirm.Checked)return;Busy(true);status.Text="保存前再次确认环境…";try{
                if(DateTime.UtcNow-captured>TimeSpan.FromMinutes(5))throw new InvalidOperationException("快照超过 5 分钟，请重新检测确认。");
                var p=draft;await Task.Run(async()=>{var checker=new EnvironmentChecker(p);var errors=checker.Local();var network=await checker.Network(CancellationToken.None);if(errors.Count>0 || !network.Healthy)throw new InvalidOperationException("保存前环境发生改变或尚未确认，请重新检测。\r\n"+String.Join("\r\n",errors.Concat(network.Confirmed).Concat(network.Unconfirmed)));
                    var log=new AuditLog(p.LogFile);if(!log.Write("baseline_confirmed",new {baseline=p.CapturedAt,apps=p.Apps.Select(a=>a.Name).ToArray(),snapshot=p.Settings,exit=p.ExitIp}))throw new IOException("无法写入选定日志："+log.Error);p.Save(profileFile);
                });Result=p;Busy(false);DialogResult=DialogResult.OK;Close();
            }catch(Exception ex){status.Text="未保存："+ex.Message;UI.Error(ex.Message);}finally{if(!IsDisposed)Busy(false);}
        }
        public static string Describe(Profile p)
        {
            var lines=new List<string>{"确认时间："+p.CapturedAt,"预期出口 IP："+p.ExitIp,"本机代理："+p.ProxyHost+":"+p.ProxyPort,"代理程序："+p.ProxyExecutable,"日志文件："+p.LogFile,"", "保护范围（紧急关闭会丢失未保存工作）："};
            foreach(var a in p.Apps){lines.Add(a.Name+"："+a.Executable);lines.Add("  专属目录："+(a.Folder??"未启用；仅主进程与观察到的后代"));foreach(var s in a.Services)lines.Add("  专属服务："+s.Name+" / "+s.Executable);}
            lines.Add("");foreach(var pair in p.Settings)lines.Add(pair.Key+"："+pair.Value);lines.Add("");lines.Add("正常时间流逝、自动夏令时切换不算异常。未选择的浏览器配置、字体和全流量路径不在检测范围。");return String.Join("\r\n",lines);
        }
        public void PreviewPage(int page,Profile p){review.Text=Describe(p);tabs.SelectedIndex=page;}
    }
    public sealed class AlertForm : Form
    {
        readonly Label heading=UI.Label("环境异常，请先暂停使用保护软件",65);readonly TextBox details=UI.Details();readonly Func<Task> stop;
        readonly string original;readonly DateTimeOffset first;
        public AlertForm(HealthState initial,Func<Task> emergency)
        {
            stop=emergency;first=initial.Time;original=String.Join("\r\n",initial.Issues.Concat(initial.LogError==null?new string[0]:new[]{"日志无法保存："+initial.LogError}));
            UI.Base(this,"EnvGuard · 环境预警",760,430);Padding=new Padding(24);heading.Font=new Font(Font.FontFamily,17,FontStyle.Bold);heading.ForeColor=UI.Red;
            var note=UI.Label("预警不会自动断网或关闭软件。红色按钮将立即执行强制关闭，未保存工作会丢失。",58);
            var buttons=UI.Buttons();var kill=UI.Button("紧急关闭保护软件",async(s,e)=>{await stop();});kill.BackColor=UI.Red;kill.ForeColor=Color.White;kill.FlatStyle=FlatStyle.Flat;buttons.Controls.Add(kill);buttons.Controls.Add(UI.Button("知道了",(s,e)=>Close()));Controls.Add(details);Controls.Add(note);Controls.Add(heading);Controls.Add(buttons);UpdateState(initial);
        }
        public void UpdateState(HealthState state){if(IsDisposed)return;bool restored=state.Healthy;heading.Text=restored?"检查项已恢复（保留原始原因）":"环境异常，请先暂停使用保护软件";heading.ForeColor=restored?UI.Green:UI.Red;details.Text="首次异常："+first.ToString("G")+"\r\n"+original+"\r\n\r\n最新检查："+state.Time.ToString("G")+"\r\n"+state.Summary+"\r\n"+String.Join("\r\n",state.Issues)+"\r\n\r\n原始异常与恢复记录已尝试写入日志；日志失败会明确提示。";}
    }
    public sealed class MainForm : Form
    {
        readonly Profile profile;readonly string file;readonly MonitorEngine engine;
        readonly Label status=UI.Label("正在检测，请等待",60),summary=UI.Label("",90);readonly TextBox details=UI.Details();readonly NotifyIcon tray;
        readonly Button kill;AlertForm alert;bool stopping,reconfigure;public bool Reconfigure {get{return reconfigure;}}
        public MainForm(Profile p,string profileFile,bool preview)
        {
            profile=p;file=profileFile;UI.Base(this,"EnvGuard · 环境预警与紧急关闭",880,610);Padding=new Padding(24);status.Font=new Font(Font.FontFamily,20,FontStyle.Bold);
            summary.Text="保护软件："+String.Join("、",p.Apps.Select(a=>a.Name))+"\r\n预期出口："+p.ExitIp+"   |   时区："+(p.Settings.ContainsKey("Windows 时区")?p.Settings["Windows 时区"]:"未确认")+"\r\n模式：只预警 + 手动紧急关闭；不会自动启动或自动关闭软件。";
            var footer=UI.Label("日志："+p.LogFile+"\r\n与基准一致不等于保证所有流量受保护，也不保证账号不会被限制。",72);footer.Dock=DockStyle.Bottom;
            var buttons=UI.Buttons();kill=UI.Button("紧急关闭保护软件",async(s,e)=>await Emergency());kill.FlatStyle=FlatStyle.Flat;kill.BackColor=UI.Red;kill.ForeColor=Color.White;buttons.Controls.Add(kill);
            buttons.Controls.Add(UI.Button("查看进程范围",(s,e)=>{if(engine!=null)ShowText("已识别进程（不含未知客体）",engine.Inspect());}));
            buttons.Controls.Add(UI.Button("重新配置",(s,e)=>{if(MessageBox.Show("重新配置期间会暂停预警。请先暂停使用保护软件，再继续。","EnvGuard",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)==DialogResult.OK){reconfigure=true;Close();}}));
            buttons.Controls.Add(UI.Button("查看日志",(s,e)=>{try{if(File.Exists(profile.LogFile))Process.Start("explorer.exe","/select,\""+profile.LogFile+"\"");else UI.Error("日志还不存在。");}catch(Exception ex){UI.Error(ex.Message);}}));
            buttons.Controls.Add(UI.Button("检查更新",async(s,e)=>await CheckUpdate()));Controls.Add(details);Controls.Add(summary);Controls.Add(status);Controls.Add(footer);Controls.Add(buttons);
            var menu=new ContextMenuStrip();menu.Items.Add("显示窗口",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("紧急关闭保护软件",null,async(s,e)=>await Emergency());menu.Items.Add("退出预警（不关闭软件）",null,(s,e)=>Close());
            tray=new NotifyIcon{Icon=SystemIcons.Shield,Text="EnvGuard · 只预警，手动关闭",Visible=!preview,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();};Resize+=(s,e)=>{if(WindowState==FormWindowState.Minimized && !preview)Hide();};
            if(!preview){engine=new MonitorEngine(p);engine.Changed+=(state,show)=>{if(IsDisposed || !IsHandleCreated)return;try{BeginInvoke((Action)(()=>Render(state,show)));}catch{}};Shown+=(s,e)=>engine.Start();}
            FormClosing+=(s,e)=>{if(stopping){e.Cancel=true;return;}if(!preview && !reconfigure && MessageBox.Show("退出后不再监测，也不会关闭保护软件。确定退出？","EnvGuard",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)e.Cancel=true;};
            FormClosed+=(s,e)=>{if(alert!=null)alert.Close();tray.Dispose();if(engine!=null)engine.Dispose();};
        }
        public void Render(HealthState state,bool show){status.Text=state.Summary;status.ForeColor=state.Healthy?UI.Green:state.Issues.Length>0 || state.LogError!=null?UI.Red:UI.Blue;details.Text="检查时间："+state.Time.ToString("G")+"\r\n已识别存活进程："+state.ProcessCount+"\r\n"+String.Join("\r\n",state.Issues)+ (state.LogError!=null?"\r\n日志失败："+state.LogError:"")+"\r\n\r\n"+(state.Healthy?"本轮所选检查项通过。":"检测未通过或尚未确认；不要把检测结果当作全流量隔离证明。");
            if(alert!=null && !alert.IsDisposed)alert.UpdateState(state);if(show){tray.ShowBalloonTip(5000,"EnvGuard 环境异常",state.Summary+"；请查看原因。需要停止时点击紧急关闭。",ToolTipIcon.Warning);if(alert==null || alert.IsDisposed){alert=new AlertForm(state,Emergency);alert.Show(this);}alert.Activate();}}
        async Task Emergency(){if(stopping || engine==null)return;stopping=true;kill.Enabled=false;status.Text="正在强制关闭并核对残留…";try{var result=await Task.Run(()=>engine.Emergency());ShowText(result.Success?"配置范围内未发现残留":"结束未完全确认，请查看原因","耗时："+result.ElapsedMs+" ms\r\n停止尝试："+result.StopAttempts+"\r\n已识别残留："+result.Remaining+"\r\n"+String.Join("\r\n",result.Errors)+"\r\n\r\n"+result.Coverage);}catch(Exception ex){UI.Error(ex.Message);}finally{stopping=false;kill.Enabled=true;}}
        async Task CheckUpdate(){try{if(String.IsNullOrEmpty(profile.GitHubRepository)){UI.Error("尚未设置 GitHub 仓库。发布后在重新配置中填入账号/仓库。");return;}status.Text="正在通过指定代理查询正式发布…";
            using(var client=EnvironmentChecker.Client(profile)){client.Timeout=TimeSpan.FromSeconds(15);string url="https://api.github.com/repos/"+profile.GitHubRepository+"/releases/latest";using(var response=await client.GetAsync(url)){
                response.EnsureSuccessStatusCode();var root=new JavaScriptSerializer().DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string,object>;if(root==null)throw new InvalidDataException("更新响应无效。");string tag=Convert.ToString(root["tag_name"]);Version latest,current;
                if(!Version.TryParse(tag.TrimStart('v'),out latest) || !Version.TryParse(Program.Version,out current))throw new InvalidDataException("发布版本格式无效。");
                if(latest>current){if(MessageBox.Show("有新版本 "+tag+"。打开发布页下载？\r\n不会自动替换程序；退出监测后替换 EXE，原本地配置保留。","EnvGuard",MessageBoxButtons.YesNo)==DialogResult.Yes)Process.Start(new ProcessStartInfo("https://github.com/"+profile.GitHubRepository+"/releases/latest"){UseShellExecute=true});}else MessageBox.Show("当前版本 "+Program.Version+"；未发现更高正式版本。","EnvGuard");
            }}
        }catch(Exception ex){UI.Error("更新检查失败："+ex.Message);}finally{if(engine!=null && engine.Latest!=null)Render(engine.Latest,false);}}
        static void ShowText(string title,string text){using(var form=new Form()){UI.Base(form,title,820,460);form.Padding=new Padding(20);var box=UI.Details();box.Text=text;form.Controls.Add(box);form.ShowDialog();}}
    }
}
