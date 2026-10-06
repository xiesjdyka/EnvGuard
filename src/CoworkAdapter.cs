using System;
using System.Collections.Generic;
using System.ServiceProcess;
using ClaudeDesktopGuard.NativeCompute;
namespace EnvGuard
{
    public static class CoworkAdapter
    {
        public static List<string> Stop(ServiceTarget service)
        {
            var errors=new List<string>();
            if(service.Hash!=GuardComputeSystems.SupportedServiceSha256){
                // Unknown matcher versions never terminate guests. A stopped,
                // verified service plus an entirely empty HCS inventory is still
                // direct evidence of absence, without guessing any guest identity.
                try{Services.Verify(service);using(var controller=new ServiceController(service.Name)){controller.Refresh();if(controller.Status==ServiceControllerStatus.Stopped && CanVerifyEmptyInventory(true,GuardComputeSystems.Enumerate(2000).Length))return errors;}}catch(Exception ex){errors.Add("Cowork 客体复查："+ex.Message);}
                errors.Add("此版本 Cowork 虚拟机身份规则未经验证；仅处理已配置的主机进程和服务，不能确认客体已结束。");return errors;
            }
            try{Services.Verify(service);GuardComputeSystems.SupportedServicePath=service.Executable;var result=GuardComputeSystems.StopCowork(8000,true);errors.AddRange(result.Errors);if(!result.Success && result.Errors.Count==0)errors.Add("Cowork 虚拟机结束未确认。");}catch(Exception ex){errors.Add("Cowork："+ex.Message);}return errors;
        }
        internal static bool CanVerifyEmptyInventory(bool serviceStopped,int count){return serviceStopped && count==0;}
    }
}
