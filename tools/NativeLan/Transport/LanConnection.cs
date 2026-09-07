// LanConnection.cs — bounded text frames and one serialized WebSocket writer.
// A slow reader is disconnected instead of accumulating an unbounded state queue.
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Transport;

internal sealed class LanConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly LanOptions _options;
    private readonly Channel<string> _outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _writer;
    public string? SeatId { get; set; }
    public Guid Id { get; } = Guid.NewGuid();
    public LanConnection(WebSocket socket,LanOptions options) { _socket = socket; _options = options; }
    public void StartWriter() => _writer = WriteLoop();
    public bool Send(JObject message)
    {
        var text = message.ToString(Formatting.None);
        if (Encoding.UTF8.GetByteCount(text) > _options.MaximumOutboundBytes || !_outbound.Writer.TryWrite(text)) { Abort(); return false; }
        return true;
    }
    private async Task WriteLoop()
    {
        try { await foreach (var text in _outbound.Reader.ReadAllAsync(_lifetime.Token)) await _socket.SendAsync(Encoding.UTF8.GetBytes(text),WebSocketMessageType.Text,true,_lifetime.Token); }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException) { Abort(); }
    }
    public async Task<JObject?> Receive(CancellationToken shutdown)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown,_lifetime.Token);
        if (SeatId == null) cancellation.CancelAfter(_options.HandshakeTimeout);
        using var bytes = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer),cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new ProtocolException("text_required");
            if (bytes.Length + result.Count > _options.MaximumInboundBytes) throw new ProtocolException("message_too_large");
            bytes.Write(buffer,0,result.Count); if (result.EndOfMessage) break;
        }
        var text = new UTF8Encoding(false,true).GetString(bytes.ToArray());
        using var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 24, DateParseHandling = DateParseHandling.None };
        var value = JObject.Load(reader,new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new ProtocolException("one_message_required");
        return value;
    }
    public void Abort() { _outbound.Writer.TryComplete(); _lifetime.Cancel(); _socket.Abort(); }
    public async Task CompleteAsync()
    {
        _outbound.Writer.TryComplete();
        if (_writer != null) try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { Abort(); }
    }
    public async ValueTask DisposeAsync()
    {
        Abort(); if (_writer != null) await _writer; _socket.Dispose(); _lifetime.Dispose();
    }
}
internal sealed class ProtocolException(string code) : Exception(code);
