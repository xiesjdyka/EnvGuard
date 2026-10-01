// Original motion / sound design; no speech, external assets or real device data.
// Build: Framework64 csc.exe /r:System.Drawing.dll /out:Promo.exe Promo.cs
// Run: Promo.exe <frame-directory> [--stills] or Promo.exe <sound.wav> --sound
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
public static class Promo
{
    const int W=1280,H=720,Fps=24,Seconds=32,Rate=48000;
    static readonly Color Paper=Color.FromArgb(248,246,241),Ink=Color.FromArgb(26,37,38),Mute=Color.FromArgb(102,114,113),Teal=Color.FromArgb(0,115,102),Red=Color.FromArgb(199,60,42),Line=Color.FromArgb(220,226,219),Mint=Color.FromArgb(211,234,193);
    static Graphics g;static double[] starts={0,4,8,13,18,23,27};
    static double Clamp(double t){return Math.Max(0,Math.Min(1,t));}
    static float Ease(double t){return (float)(1-Math.Pow(1-Clamp(t),3));}
    static void Text(string s,float x,float y,float size,Color c,bool bold=true,float width=0)
    {
        using(var f=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(c))using(var fmt=(StringFormat)StringFormat.GenericTypographic.Clone()){
            fmt.FormatFlags|=StringFormatFlags.NoWrap;
            if(width>0){fmt.Alignment=StringAlignment.Center;g.DrawString(s,f,b,new RectangleF(x,y,width,size*1.65f),fmt);}else g.DrawString(s,f,b,x,y,fmt);
        }
    }
    static void Rect(float x,float y,float w,float h,Color c,float r=16)
    {
        using(var b=new SolidBrush(c))using(var p=new GraphicsPath()){
            if(r==0)p.AddRectangle(new RectangleF(x,y,w,h));else {p.AddArc(x,y,2*r,2*r,180,90);p.AddArc(x+w-2*r,y,2*r,2*r,270,90);p.AddArc(x+w-2*r,y+h-2*r,2*r,2*r,0,90);p.AddArc(x,y+h-2*r,2*r,2*r,90,90);p.CloseFigure();}g.FillPath(b,p);
        }
    }
    static void Stroke(float x,float y,float xx,float yy,Color c,float w=3){using(var p=new Pen(c,w)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,x,y,xx,yy);}}
    static void Circle(float x,float y,float r,Color c,bool fill=true,float w=3){using(var p=new Pen(c,w))using(var b=new SolidBrush(c)){if(fill)g.FillEllipse(b,x-r,y-r,2*r,2*r);else g.DrawEllipse(p,x-r,y-r,2*r,2*r);}}
    static void Tick(float x,float y,Color c,float s=1){Stroke(x-12*s,y,x-3*s,y+9*s,c,4*s);Stroke(x-3*s,y+9*s,x+16*s,y-12*s,c,4*s);}
    static void Shield(float x,float y,float s,Color c)
    {var state=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(s,s);using(var p=new GraphicsPath()){p.AddLines(new[]{new PointF(0,-80),new PointF(65,-57),new PointF(58,20)});p.AddBezier(58,20,48,56,18,77,0,90);p.AddBezier(0,90,-18,77,-48,56,-58,20);p.AddLine(-58,20,-65,-57);p.CloseFigure();using(var b=new SolidBrush(c))g.FillPath(b,p);}Tick(0,0,Paper,1.8f);g.Restore(state);}
    static void Cursor(float x,float y){var pts=new[]{new PointF(x,y),new PointF(x,y+33),new PointF(x+9,y+25),new PointF(x+20,y+41),new PointF(x+28,y+36),new PointF(x+19,y+21),new PointF(x+33,y+20)};using(var b=new SolidBrush(Paper))using(var p=new Pen(Ink,2)){g.FillPolygon(b,pts);g.DrawPolygon(p,pts);}}
    static void Label(string title,string sub,double t,bool dark=false)
    {float lift=22*(1-Ease(t/.7));Text(title,64,138+lift,57,dark?Paper:Ink);Text(sub,67,222+lift,27,dark?Mint:Mute,false);}
    static void Hook(double t)
    {
        Text("用 Claude，",64,158,72,Ink);Text("担心突然封号？",64,254,72,Ink);
        Text("先盯住代理与环境变化。",68,385,31,Mute,false);
        bool bad=t>1.65;Circle(967,343,170,bad?Color.FromArgb(248,229,218):Mint);Circle(967,343,124,bad?Red:Teal,false,4);
        for(int i=0;i<50;i++){float x=837+i*5.3f;float y=343+(float)(Math.Sin(i*.34+t*2)*(bad?28:8));if(i>0&&(!bad||i<22||i>29))Stroke(x-5.3f,343+(float)(Math.Sin((i-1)*.34+t*2)*(bad?28:8)),x,y,bad?Red:Teal,4);}
        if(bad){Text("!",900,432,52,Red,true,134);Text("代理 / 出口变化",825,558,26,Red,true,284);}else Text("连接正常",825,558,26,Teal,true,284);
    }
    static void Intro(double t)
    {
        Text("环境有变化，",64,160,65,Paper);Text("别等用完才知道。",64,255,65,Paper);
        Text("EnvGuard",68,414,64,Mint);Text("环境预警 + 手动紧急关闭",68,507,28,Paper,false);
        Circle(1000,352,148,Color.FromArgb(45,66,63));Circle(1000,352,186,Color.FromArgb(72,91,81),false,2);Shield(1000,350,1.6f,Mint);
        for(int i=0;i<3;i++){double a=t*.3+i*Math.PI*2/3;Circle(1000+(float)Math.Cos(a)*186,352+(float)Math.Sin(a)*186,6,Mint);}
    }
    static void Setup(double t)
    {
        Label("第一次，不用填一堆。","选 Claude → 确认端口 → 核对并保存",t);
        Rect(65,303,1150,291,Color.White,22);
        Text("本机代理端口",103,345,27,Ink);Rect(357,331,385,59,Paper,9);Text("10808",383,343,30,Teal);Rect(773,331,393,59,Ink,9);Text("v2rayN 左下角：mixed",773,349,23,Paper,true,393);
        Stroke(104,413,1170,413,Line,2);Text("日志存放位置",103,446,27,Ink);Text("默认已填好，不用改",383,446,27,Mute,false);Tick(1118,464,Teal);
        Text("更多检查",104,537,25,Mute,false);Text("可选，先跳过也可以",383,537,25,Mute,false);
        if(t>3.0){Rect(789,608,427,52,Teal,12);Text("自己核对，再确认保存",789,619,24,Paper,true,427);}
    }
    static void Watch(double t)
    {
        Label("出口变了，立即看见。","出口 IP 变化会提醒；连续两轮网络未确认也会提醒。",t);
        bool bad=t>1.7;Rect(65,310,473,273,Color.White,22);Text("保存的环境",97,344,25,Mute,false);Text("出口 A",97,408,56,Ink);Text("时区 / 区域设置",99,509,25,Mute,false);
        Stroke(565,446,711,446,bad?Red:Teal,4);Text(bad?"≠":"=",588,386,62,bad?Red:Teal);
        Rect(741,310,473,273,bad?Red:Teal,22);Text("当前检查",773,344,25,Paper,false);Text(bad?"出口 B":"出口 A",773,408,56,Paper);Text(bad?"发现变化 · 弹窗提醒":"所选检查项一致",775,509,25,Paper,false);
        if(bad){float f=Ease((t-1.7)/.45);Circle(1170,318,30*f,Ink);Text("!",1140,291,39,Paper,true,60);}
    }
    static void Stop(double t)
    {
        Label("异常了？你一键叫停。","手动点击红色按钮，强制关闭已确认的软件范围。",t);
        bool clicked=t>2.25;Rect(65,340,501,231,Ink,22);Text("Claude",103,371,43,Paper);Rect(95,452,441,77,clicked?Teal:Red,12);Text(clicked?"关闭结果已记录":"紧急关闭保护软件",95,475,29,Paper,true,441);
        string[] names={"主进程","已识别子进程","已确认专属服务"};for(int i=0;i<3;i++){float y=335+i*89;bool done=t>2.25+i*.14;Stroke(639,y+56,1209,y+56,Line,2);Text(names[i],678,y+7,30,done?Mute:Ink);if(done)Tick(1173,y+28,Teal);else Circle(1173,y+28,8,Red);}
        if(t>.6){float f=Ease((t-.6)/1.65);float x=688-267*f,y=605-116*f;Cursor(x,y);if(clicked&&t<2.8)Circle(421,489,20+100*Ease((t-2.25)/.55),Red,false,2);}
    }
    static void Logs(double t)
    {
        Label("每一次提醒，都有记录。","提醒原因、恢复时间、关闭结果，留在本地日志。",t);
        string[] a={"发现异常","环境恢复","紧急关闭"};string[] b={"当时发生了什么","何时重新通过检查","关闭结果与未完成项"};
        for(int i=0;i<3;i++){float f=Ease((t-i*.35)/.55),y=324+i*92;var st=g.Save();g.TranslateTransform(28*(1-f),0);Circle(90,y+22,10,i==0?Red:Teal);if(i<2)Stroke(90,y+40,90,y+72,Line,3);Text(a[i],130,y,34,Ink);Text(b[i],443,y+4,27,Mute,false);g.Restore(st);}
        Rect(911,330,282,245,Ink,20);Text("日志",911,369,50,Mint,true,282);Text("你选的文件夹",911,462,27,Paper,false,282);
    }
    static void End(double t)
    {
        Shield(640,175,.75f,Mint);Text("EnvGuard",64,277,94,Paper,true,1152);
        Text("为 Claude 使用环境，加一层提醒。",64,418,36,Paper,true,1152);
        Text("免费开源  /  Windows  /  换电脑重新核对一次",64,480,25,Mint,false,1152);
        Rect(183,550,914,75,Mint,14);Text("github.com/xiesjdyka/EnvGuard",183,570,35,Ink,true,914);
    }
    static void Frame(double t)
    {
        int scene=0;while(scene<6&&t>=starts[scene+1])scene++;double u=t-starts[scene];bool dark=scene==1||scene==6;g.Clear(dark?Ink:Paper);
        Text("EnvGuard",64,43,26,dark?Paper:Ink);Text("CLAUDE 环境预警",958,47,19,dark?Mint:Mute,false);
        Stroke(64,676,1216,676,dark?Color.FromArgb(61,77,70):Line,3);Stroke(64,676,64+(float)(1152*t/Seconds),676,dark?Mint:Teal,3);
        var state=g.Save();g.TranslateTransform(32*(1-Ease(u/.35)),0);switch(scene){case 0:Hook(u);break;case 1:Intro(u);break;case 2:Setup(u);break;case 3:Watch(u);break;case 4:Stop(u);break;case 5:Logs(u);break;default:End(u);break;}g.Restore(state);
        double end=scene==6?Seconds:starts[scene+1],fade=Clamp((t-end+.16)/.16);if(scene<6&&fade>0)using(var b=new SolidBrush(Color.FromArgb((int)(255*fade),dark?Ink:Paper)))g.FillRectangle(b,0,0,W,H);
    }
    static void Sound(string file)
    {
        var left=new double[Seconds*Rate];var right=new double[left.Length];var rng=new Random(718);
        Action<double,double,double,double> tone=(at,hz,dur,vol)=>{int offset=(int)(at*Rate);for(int i=0;i<dur*Rate&&offset+i<left.Length;i++){double t=(double)i/Rate,env=Math.Min(1,t/.012)*Math.Exp(-t*5/dur)*(1-t/dur);double v=vol*env*(Math.Sin(2*Math.PI*hz*t)+.22*Math.Sin(2*Math.PI*hz*2*t));left[offset+i]+=v;right[offset+i]+=v*.94;}};
        Action<double> whoosh=at=>{int offset=(int)(at*Rate);double filtered=0;for(int i=0;i<.38*Rate&&offset+i<left.Length;i++){double t=(double)i/Rate,p=t/.38;filtered=.88*filtered+.12*(rng.NextDouble()*2-1);double v=.19*Math.Sin(Math.PI*p)*filtered;left[offset+i]+=v*(1-p*.45);right[offset+i]+=v*(.55+p*.45);}};
        foreach(double at in starts){whoosh(at+.05);tone(at+.10,261.63,.5,.045);tone(at+.17,392,.7,.035);}
        tone(1.65,220,.22,.17);tone(1.94,293.66,.22,.12);tone(4.4,523.25,.5,.09);tone(4.52,783.99,.65,.07);
        tone(8.8,440,.11,.07);tone(9.1,554.37,.13,.065);tone(11.05,659.25,.4,.10);tone(11.17,880,.48,.07);
        tone(14.7,329.63,.24,.15);tone(15.0,261.63,.28,.12);
        tone(20.25,180,.06,.2);for(int i=0;i<3;i++)tone(20.25+i*.14,523.25+i*110,.24,.10);
        for(int i=0;i<3;i++)tone(23.35+i*.35,440+i*110,.22,.07);
        tone(27.4,261.63,1.7,.10);tone(27.5,329.63,1.7,.08);tone(27.6,392,1.9,.08);tone(30.5,523.25,1.1,.075);tone(30.6,659.25,1.3,.055);
        using(var writer=new BinaryWriter(File.Create(file))){int bytes=left.Length*4;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(Rate);writer.Write(Rate*4);writer.Write((short)4);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);for(int i=0;i<left.Length;i++){writer.Write((short)(Math.Max(-.95,Math.Min(.95,left[i]))*32767));writer.Write((short)(Math.Max(-.95,Math.Min(.95,right[i]))*32767));}}
    }
    public static int Main(string[] args)
    {
        if(args.Length==2&&args[1]=="--sound"){Sound(args[0]);return 0;}Directory.CreateDirectory(args[0]);bool stills=args.Length>1&&args[1]=="--stills";
        using(var bitmap=new Bitmap(W,H,PixelFormat.Format24bppRgb))using(g=Graphics.FromImage(bitmap)){g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.CompositingQuality=CompositingQuality.HighQuality;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            if(stills){double[] times={2.8,6,11.5,16,21.5,25.5,30};for(int i=0;i<times.Length;i++){Frame(times[i]);bitmap.Save(Path.Combine(args[0],"scene_"+i+".png"));}}
            else for(int i=0;i<Fps*Seconds;i++){Frame((double)i/Fps);bitmap.Save(Path.Combine(args[0],"frame_"+i.ToString("D4")+".png"));if(i%120==0)Console.WriteLine("Frame "+i+" / "+Fps*Seconds);}
        }return 0;
    }
}
