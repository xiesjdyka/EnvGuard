using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
public static class Fixture
{
    public static void Main(string[] args){if(args.Length==3 && args[0]=="--parent"){using(var child=Process.Start(new ProcessStartInfo(args[1]){UseShellExecute=false,CreateNoWindow=true})){File.WriteAllText(args[2],child.Id.ToString());Thread.Sleep(120000);}}else Thread.Sleep(120000);}
}
