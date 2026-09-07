using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using AshenSpire.Transport;
sealed class Peer : IAsyncDisposable
{
 private readonly ClientWebSocket _socket=new();private readonly Channel<JObject> _messages=Channel.CreateUnbounded<JObject>();private Task? _reader;private bool _disposed;public bool Closed {get;private set;}
 public static async Task<Peer> Open(Uri uri){var peer=new Peer();peer._socket.Options.AddSubProtocol(LanHost.Subprotocol);await peer._socket.ConnectAsync(uri,CancellationToken.None);peer._reader=peer.Read();return peer;}
 public Task Raw(string text)=>_socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),WebSocketMessageType.Text,true,CancellationToken.None);
 public Task Send(string type,JObject payload,string request){foreach(var p in payload.Properties().Where(p=>p.Value.Type==JTokenType.Null).ToArray())p.Remove();return Raw(new JObject{["v"]=1,["type"]=type,["requestId"]=request,["payload"]=payload}.ToString());}
 public async Task<JObject> Next(Func<JObject,bool> predicate){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(await _messages.Reader.WaitToReadAsync(timeout.Token))while(_messages.Reader.TryRead(out var message))if(predicate(message))return message;throw new ChannelClosedException();}
 private async Task Read(){try{var buffer=new byte[8192];while(true){using var data=new MemoryStream();WebSocketReceiveResult part;do{part=await _socket.ReceiveAsync(buffer,CancellationToken.None);if(part.MessageType==WebSocketMessageType.Close)return;data.Write(buffer,0,part.Count);}while(!part.EndOfMessage);await _messages.Writer.WriteAsync(JObject.Parse(Encoding.UTF8.GetString(data.ToArray())));}}catch(Exception e)when(e is WebSocketException or ObjectDisposedException or OperationCanceledException){}finally{Closed=true;_messages.Writer.TryComplete();}}
 public async ValueTask DisposeAsync(){if(_disposed)return;_disposed=true;_socket.Abort();if(_reader!=null)await _reader;_socket.Dispose();}
}


