using System;
using System.Linq;

namespace EnvGuard
{
    // Only verified exit mismatch or TWO consecutive timed-out rounds can
    // authorize automatic closure. Local/log/identity warnings are not inputs.
    internal sealed class AutomaticProtectionPolicy
    {
        public int ConsecutiveTimeouts {get;private set;}
        public string[] Causes {get;private set;}
        bool executed;
        public AutomaticProtectionPolicy(){Causes=new string[0];}
        public void Observe(NetworkResult result)
        {
            if(result.Healthy){ConsecutiveTimeouts=0;Causes=new string[0];executed=false;return;}
            ConsecutiveTimeouts=result.TimeoutCount>0?ConsecutiveTimeouts+1:0;
            Causes=result.Confirmed.Count>0?result.Confirmed.ToArray():
                ConsecutiveTimeouts>=NetworkTiming.FailureThreshold?new[]{"网络检测连续两轮超时，自动紧急关闭。"}:new string[0];
        }
        public bool Claim(bool enabled)
        {
            if(!enabled || executed || Causes.Length==0)return false;
            executed=true;return true;
        }
    }
    public sealed class AutomaticProtectionResult
    {
        public string[] Causes;
        public StopResult Result;
        public string Message {get{return Result.Success?"已自动执行紧急保护":"已自动尝试紧急保护，结束未完全确认，请查看原因";}}
    }
}
