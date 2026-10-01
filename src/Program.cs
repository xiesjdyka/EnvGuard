using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
[assembly:System.Reflection.AssemblyVersion("1.2.2.0")]
[assembly:System.Reflection.AssemblyFileVersion("1.2.2.0")]
namespace EnvGuard
{
    public static class Program
    {
        public const string Version="1.2.2";
        public const string DefaultRepository="xiesjdyka/EnvGuard";
        [STAThread] public static int Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{
                if(args.Length>0 && args[0]=="--self-test")return Tests.Run(args.Length>1?args[1]:null);
                if(args.Length>0 && args[0]=="--ui-smoke-test")return Tests.UIShots(args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"EnvGuard-ui"));
                if(args.Length!=0)throw new ArgumentException("支持参数：--self-test [测试目录]、--ui-smoke-test [图片目录]；日常使用直接双击。");
                using(var identity=WindowsIdentity.GetCurrent())if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)){
                    try{Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location){UseShellExecute=true,Verb="runas"});return 0;}catch(System.ComponentModel.Win32Exception){UI.Error("需要在启动时确认管理员权限，才能监测服务并快速执行紧急关闭。权限未确认，本次未启动监测。");return 2;}
                }
                bool created;using(var mutex=new Mutex(true,@"Local\EnvGuard-"+EnvironmentChecker.Sid(),out created)){
                    if(!created){UI.Error("EnvGuard 已在运行，请从任务栏托盘打开现有窗口，避免重复提醒。");return 0;}
                    string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EnvGuard","profiles","default.json");Profile p=null;
                    if(File.Exists(file))try{p=Profile.Load(file);if(p.UserSid!=EnvironmentChecker.Sid() || p.Computer!=Environment.MachineName){UI.Error("这份配置来自其他设备或用户，必须在本机重新确认基准。");p=null;}}catch(Exception ex){UI.Error("原配置不可用，将进入重新配置："+ex.Message);}
                    bool configure=p==null;
                    while(true){
                        if(configure)using(var setup=new SetupForm(file,p)){if(setup.ShowDialog()!=DialogResult.OK)return 0;p=setup.Result;}
                        using(var main=new MainForm(p,file,false)){Application.Run(main);if(!main.Reconfigure)break;configure=true;}
                    }
                    mutex.ReleaseMutex();return 0;
                }
            }catch(Exception ex){MessageBox.Show(ex.Message,"EnvGuard 启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
