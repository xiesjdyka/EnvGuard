using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EnvGuard
{
    internal static class NetworkPolicyTests
    {
        static int count;
        static void Check(bool pass,string reason){count++;if(!pass)throw new Exception("Network policy: "+reason);}
        static HttpResponseMessage Reply(string ip){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ip)};}
        static Task<HttpResponseMessage> Good(){return Task.FromResult(Reply("192.0.2.10"));}
        sealed class RecordingHandler : HttpMessageHandler
        {
            public readonly List<string> Hosts=new List<string>();
            public Func<string,int,CancellationToken,Task<HttpResponseMessage>> Response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Hosts.Add(request.RequestUri.Host);return Response(request.RequestUri.Host,Hosts.Count,token);
            }
        }
        sealed class GateHandler : HttpMessageHandler
        {
            public readonly List<string> Hosts=new List<string>();
            public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>();
            public readonly TaskCompletionSource<HttpResponseMessage> Slow=new TaskCompletionSource<HttpResponseMessage>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Hosts.Add(request.RequestUri.Host);
                if(Hosts.Count>1)return Reply("192.0.2.10");
                Started.TrySetResult(true);
                using(token.Register(()=>Slow.TrySetCanceled()))return await Slow.Task.ConfigureAwait(false);
            }
        }
        public static int Run(Profile p)
        {
            count=0;Check(NetworkTiming.PollIntervalMs==11000,"eleven-second interval");Check(NetworkTiming.RequestTimeoutMs==8000,"eight-second request timeout");Check(NetworkTiming.FailureThreshold==2,"two failed rounds");
            var schedule=new NetworkSchedule();Check(schedule.Due(0,false),"first round immediate");schedule.Started(0);
            Check(!schedule.Due(8000,false)&&!schedule.Due(10999,false),"no early retry after timeout");
            Check(schedule.Due(11000,false),"next round at eleven seconds from start");Check(!schedule.Due(22000,true),"no overlapping round");
            schedule.Started(25000);Check(schedule.NextStartMs==36000&&!schedule.Due(25001,false),"no queued catch-up rounds");
            using(var handler=new RecordingHandler{Response=(host,n,token)=>Good()})using(var client=new HttpClient(handler)){
                var checker=new EnvironmentChecker(p);
                for(int n=0;n<6;n++){
                    var result=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                    string expected=n%2==0?"checkip.amazonaws.com":"api.ipify.org";
                    Check(handler.Hosts.Count==n+1,"exactly one request per round");
                    Check(handler.Hosts[n]==expected&&result.Endpoint==expected,"Amazon first, ipify second, alternating thereafter");
                    Check(result.Healthy&&result.Addresses.Length==1,"fresh single-provider match healthy; no stale sibling required");
                }
                using(var otherHandler=new RecordingHandler{Response=(host,n,token)=>Good()})using(var otherClient=new HttpClient(otherHandler)){
                    new EnvironmentChecker(p).NetworkUsing(otherClient,CancellationToken.None).GetAwaiter().GetResult();
                    Check(otherHandler.Hosts[0]=="checkip.amazonaws.com","new monitor resets order to Amazon");
                }
            }
            foreach(bool explicitTimeout in new[]{false,true})using(var handler=new RecordingHandler{Response=(host,n,token)=>{
                if(n==4)return Good();if(explicitTimeout)throw new TimeoutException("fixture");throw new TaskCanceledException("fixture");
            }})using(var client=new HttpClient(handler)){
                var checker=new EnvironmentChecker(p);var policy=new AlertPolicy();
                var first=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(first.TimeoutCount==1&&first.Unconfirmed.Count==1,"timeout counted once per round");
                Check(!policy.Apply(first)&&policy.SoftFailures==1,"first timeout is review only");
                Check(!policy.NewIncident(new string[0]),"first timeout no popup");
                var second=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(handler.Hosts[0]=="checkip.amazonaws.com"&&handler.Hosts[1]=="api.ipify.org","failure advances provider");
                Check(policy.Apply(second)&&policy.SoftFailures==2,"cross-provider second failure alerts");
                Check(policy.NewIncident(second.Unconfirmed),"second failure popup");
                var third=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(policy.Apply(third)&&!policy.NewIncident(third.Unconfirmed),"alternation does not repeat same timeout popup");
                var fourth=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(!policy.Apply(fourth)&&policy.SoftFailures==0,"success clears streak");policy.NewIncident(new string[0]);
                var fifth=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(!policy.Apply(fifth)&&policy.SoftFailures==1,"new streak after recovery");
                Check(handler.Hosts.Count==5,"no fallback or retries");
            }
            using(var handler=new RecordingHandler{Response=(host,n,token)=>n==1?Good():Task.FromResult(Reply("192.0.2.11"))})using(var client=new HttpClient(handler)){
                var checker=new EnvironmentChecker(p);var policy=new AlertPolicy();
                policy.Apply(checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult());
                NetworkResult notified=null;
                var changed=checker.NetworkUsing(client,CancellationToken.None,r=>notified=r).GetAwaiter().GetResult();
                Check(changed.Confirmed.Count>0&&notified==changed,"changed IP published this round");
                Check(policy.Apply(changed)&&policy.SoftFailures==0,"mismatch bypasses debounce");
                Check(policy.NewIncident(changed.Confirmed)&&!policy.NewIncident(changed.Confirmed),"single mismatch incident");
                Check(handler.Hosts.Count==2&&changed.Endpoint=="api.ipify.org","no sibling request on mismatch");
            }
            using(var handler=new GateHandler())using(var client=new HttpClient(handler))using(var stop=new CancellationTokenSource()){
                var canceled=new EnvironmentChecker(p).NetworkUsing(client,stop.Token);Check(handler.Started.Task.Wait(1000),"cancel fixture started");
                Check(handler.Hosts.Count==1&&!canceled.IsCompleted,"no parallel probe while pending");stop.Cancel();
                bool propagated=false;try{canceled.GetAwaiter().GetResult();}catch(OperationCanceledException){propagated=true;}
                Check(propagated,"shutdown cancellation not network failure");
            }
            using(var handler=new GateHandler())using(var client=new HttpClient(handler){Timeout=TimeSpan.FromMilliseconds(NetworkTiming.RequestTimeoutMs)}){
                var timer=Stopwatch.StartNew();var checker=new EnvironmentChecker(p);var result=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(result.TimeoutCount==1&&result.Unconfirmed.Count==1,"real timeout unconfirmed");
                Check(timer.ElapsedMilliseconds>=7500&&timer.ElapsedMilliseconds<11000,"eight-second deadline");
                Check(handler.Hosts.Count==1,"no fallback on timeout");
                var next=checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(next.Healthy&&next.Endpoint=="api.ipify.org","next provider after actual timeout");
            }
            using(var handler=new RecordingHandler{Response=async(host,n,token)=>{await Task.Delay(6000,token).ConfigureAwait(false);return Reply("192.0.2.10");}})
            using(var client=new HttpClient(handler){Timeout=TimeSpan.FromMilliseconds(NetworkTiming.RequestTimeoutMs)}){
                var timer=Stopwatch.StartNew();var result=new EnvironmentChecker(p).NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Healthy&&result.TimeoutCount==0,"six-second handshake completes without premature cancellation");
                Check(timer.ElapsedMilliseconds>=5500&&timer.ElapsedMilliseconds<8000,"slow successful request uses full new allowance");
                Check(handler.Hosts.Count==1,"slow handshake does not trigger duplicate or direct request");
            }
            using(var handler=new GateHandler())using(var client=new HttpClient(handler)){
                var checker=new EnvironmentChecker(p);var setup=checker.NetworkSetupUsing(client,CancellationToken.None);
                Check(handler.Started.Task.Wait(1000)&&handler.Hosts.Count==1&&!setup.IsCompleted,"setup sequential, not concurrent");
                handler.Slow.TrySetResult(Reply("192.0.2.10"));var result=setup.GetAwaiter().GetResult();
                Check(result.Healthy&&result.Addresses.Length==2&&handler.Hosts.Count==2,"setup verifies both providers");
                Check(handler.Hosts[0]=="checkip.amazonaws.com"&&handler.Hosts[1]=="api.ipify.org","setup order");
                checker.NetworkUsing(client,CancellationToken.None).GetAwaiter().GetResult();
                Check(handler.Hosts[2]=="checkip.amazonaws.com","setup does not consume monitor order");
            }
            using(var handler=EnvironmentChecker.Handler(p)){
                var uri=new Uri("https://checkip.amazonaws.com");Check(handler.UseProxy&&!handler.Proxy.IsBypassed(uri),"explicit proxy required");
                Check(handler.Proxy.GetProxy(uri).Port==p.ProxyPort,"proxy port preserved");Check(!handler.AllowAutoRedirect,"no redirect/direct path");
            }
            return count;
        }
    }
}
