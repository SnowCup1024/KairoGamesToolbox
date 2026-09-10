using KairoMods.Observer;
using KairosoftGameToolbox.Services;
int failures=0;
void Check(string text,bool success){Console.WriteLine($"{(success?"PASS":"FAIL")} {text}");if(!success)failures++;}
Check("扣款7500补回15000，净增7500",MoneyReversal.Refund(129300,121800)==15000);
Check("正常收入不补回",MoneyReversal.Refund(100,200)==0);
Check("未扣款不补回",MoneyReversal.Refund(100,100)==0);
Check("余额上限不溢出",MoneyReversal.Refund(long.MaxValue,long.MaxValue-1)==0);
Check("极端差值不溢出",MoneyReversal.Refund(long.MaxValue,long.MinValue)==0);
var server=new ControlServer();server.Start();
bool enabled=false,ready=false;
var status=ModControlClient.SendAsync(2934180,ControlServer.GameDirectory,"status");
async Task<ModControlReply> Pump(Task<ModControlReply> pending){while(!pending.IsCompleted){server.Pump(r=>{if(r.Action=="activate")ready=true;if(r.Action=="set"&&ready)enabled=r.Enabled;return new(1,2934180,ControlServer.GameDirectory,ready,enabled,null);});await Task.Delay(10);}return await pending;}
var initial=await Pump(status);Check("真实管道初始关闭且未就绪",!initial.Ready&&!initial.Enabled);
await Pump(ModControlClient.SendAsync(2934180,ControlServer.GameDirectory,"activate"));
var on=await Pump(ModControlClient.SendAsync(2934180,ControlServer.GameDirectory,"set",true));Check("主线程处理后确认开启",on.Ready&&on.Enabled);
var off=await Pump(ModControlClient.SendAsync(2934180,ControlServer.GameDirectory,"set",false));Check("再次连接可确认关闭",off.Ready&&!off.Enabled);
var expired=ModControlClient.SendAsync(2934180,ControlServer.GameDirectory,"set",true);
try{await expired;}catch(Exception){ }
await Task.Delay(100);
server.Pump(r=>{enabled=true;return new(1,2934180,ControlServer.GameDirectory,true,true,null);});
Check("未处理的过期命令不会延迟生效",!enabled);
return failures;
