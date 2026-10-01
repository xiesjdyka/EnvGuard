using System;
using System.Collections.Generic;
using ClaudeDesktopGuard.NativeCompute;
namespace EnvGuard
{
    public static class CoworkAdapter
    {
        public static List<string> Stop(ServiceTarget service)
        {
            var errors=new List<string>();
            if(service.Hash!=GuardComputeSystems.SupportedServiceSha256){errors.Add("此版本 Cowork 虚拟机身份规则未经验证；仅处理已配置的主机进程和服务，不能确认客体已结束。");return errors;}
            try{Services.Verify(service);GuardComputeSystems.SupportedServicePath=service.Executable;var result=GuardComputeSystems.StopCowork(8000,true);errors.AddRange(result.Errors);if(!result.Success && result.Errors.Count==0)errors.Add("Cowork 虚拟机结束未确认。");}catch(Exception ex){errors.Add("Cowork："+ex.Message);}return errors;
        }
    }
}
