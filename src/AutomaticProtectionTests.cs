using System;
using System.IO;
using System.Web.Script.Serialization;

namespace EnvGuard
{
    internal static class AutomaticProtectionTests
    {
        static int checks;
        static void Check(bool pass,string reason){checks++;if(!pass)throw new Exception("Automatic protection: "+reason);}
        public static int Run(Profile fixture)
        {
            checks=0;Check(!new Profile().AutoKillOnAnomaly,"default false");
            var serializer=new JavaScriptSerializer();var json=serializer.Serialize(fixture);
            var legacy=serializer.Deserialize<Profile>(json.Replace("\"AutoKillOnAnomaly\":false,",""));Check(!legacy.AutoKillOnAnomaly,"legacy missing flag remains false");
            fixture.AutoKillOnAnomaly=true;Check(serializer.Deserialize<Profile>(serializer.Serialize(fixture)).AutoKillOnAnomaly,"enabled flag roundtrip");fixture.AutoKillOnAnomaly=false;
            var healthy=EnvironmentChecker.Classify(fixture.ExitIp,new[]{fixture.ExitIp},new string[1]);
            var mismatch=EnvironmentChecker.Classify(fixture.ExitIp,new[]{"192.0.2.99"},new string[1]);
            var timeout=EnvironmentChecker.Classify(fixture.ExitIp,new[]{(string)null},new[]{"timeout"});timeout.TimeoutCount=1;
            var other=EnvironmentChecker.Classify(fixture.ExitIp,new[]{(string)null},new[]{"HTTP error"});
            var policy=new AutomaticProtectionPolicy();policy.Observe(timeout);Check(!policy.Claim(true),"one timeout never kills");
            policy.Observe(timeout);Check(!policy.Claim(false),"disabled mode never kills even after two timeouts");Check(policy.Claim(true),"second consecutive timeout eligible");Check(!policy.Claim(true),"one execution per incident");
            policy.Observe(timeout);Check(!policy.Claim(true),"ongoing timeout does not loop emergency");
            policy.Observe(healthy);policy.Observe(timeout);Check(!policy.Claim(true),"success resets streak and rearms");policy.Observe(timeout);Check(policy.Claim(true),"new outage after recovery eligible");
            policy.Observe(healthy);policy.Observe(mismatch);Check(!policy.Claim(false)&&policy.Claim(true),"IP mismatch immediately eligible only when enabled");
            policy.Observe(healthy);policy.Observe(timeout);policy.Observe(other);policy.Observe(timeout);Check(!policy.Claim(true)&&policy.ConsecutiveTimeouts==1,"non-timeout error interrupts consecutive timeout streak");
            policy.Observe(other);policy.Observe(other);Check(!policy.Claim(true),"two other HTTP failures only warn, do not auto-kill");
            var failure=new StopResult();failure.Errors.Add("fixture denied");Check(!new AutomaticProtectionResult{Result=failure}.Message.Contains("已自动执行紧急保护"),"failed closure not reported as success");
            Check(new AutomaticProtectionResult{Result=new StopResult()}.Message=="已自动执行紧急保护","successful closure reported clearly");
            string blocked=Path.Combine(Path.GetDirectoryName(fixture.LogFile),"blocked-mode.json");Directory.CreateDirectory(blocked);
            using(var engine=new MonitorEngine(fixture,blocked)){
                bool failed=false;try{engine.SetAutomaticMode(true).GetAwaiter().GetResult();}catch(IOException){failed=true;}
                Check(failed&&!engine.AutoKillEnabled&&!fixture.AutoKillOnAnomaly,"failed mode save never arms automatic closure");
                engine.ObserveAutomaticNetwork(mismatch);Check(!engine.TryClaimAutomaticProtection(),"mismatch still manual after save failure");
            }
            return checks;
        }
    }
}
