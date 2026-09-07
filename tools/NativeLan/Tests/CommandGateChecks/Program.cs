using AshenSpire.Application;
var gate = new CoopCommandGate(); var checks=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
Check(!gate.IsPending,"initial"); gate.Begin("a",4);
gate.Observe(3);Check(gate.IsPending,"other peer broadcast cannot release");
gate.Accept("b");Check(gate.IsPending,"other receipt cannot release");
gate.Reject("b");Check(gate.IsPending,"other error cannot release");
gate.Accept("a");Check(gate.IsPending,"ack waits for committed view");
gate.Observe(3);Check(gate.IsPending,"stale view waits");gate.Observe(4);Check(!gate.IsPending,"own committed view releases");
gate.Begin("b",5);gate.Observe(5);Check(gate.IsPending,"view before ack waits");gate.Accept("b");Check(!gate.IsPending,"ack after view releases");
gate.Begin("c",6);gate.Reject("c");Check(!gate.IsPending,"own refusal allows corrected same sequence");
gate.Begin("d",6);gate.Reset();Check(!gate.IsPending,"disconnect clears");
gate.Begin("e",6);gate.Accept("d");gate.Observe(5);Check(gate.IsPending,"reconnect ignores old acknowledgement");
bool blocked=false;try{gate.Begin("f",6);}catch(InvalidOperationException){blocked=true;}Check(blocked,"double send blocked");
gate.Reject("e");gate.Begin("f",6);gate.Observe(7);gate.Accept("f");Check(!gate.IsPending,"later view includes commit");
gate.Accept(null);gate.Reject(null);gate.Observe(99);Check(!gate.IsPending,"idle events harmless");
foreach(var value in new[]{0,-1}){bool refused=false;try{gate.Begin("x",value);}catch(ArgumentException){refused=true;}Check(refused&&!gate.IsPending,"invalid sequence atomic");}
Console.WriteLine($"PASS {checks} client command-gate ordering checks.");
