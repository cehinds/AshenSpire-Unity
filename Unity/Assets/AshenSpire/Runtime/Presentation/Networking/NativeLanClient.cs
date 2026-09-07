// NativeLanClient.cs — attach to one dedicated network GameObject per session.
// Call Connect, send protocol JSON after Opened, and render MessageReceived state.
// This component transports bytes only: rules, costs and ownership stay on the host.
// To change limits, update the matching host protocol limits too. Do not rename
// the generated receiver object while connected: the WebGL bridge uses its name.
// Native callbacks are queued onto Update; disabling/destroying cancels the socket.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#endif
namespace AshenSpire.Presentation.Networking
{
    [DisallowMultipleComponent]
    public sealed class NativeLanClient : MonoBehaviour
    {
        public const string Subprotocol = "ashenspire.native.v1";
        private const int MaximumOutboundBytes = 16384, MaximumInboundBytes = 2097152;
        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> Closed;
        public event Action<string> Failed;
        public bool IsConnected { get; private set; }
        private int _generation;
        private readonly string _receiverName = "AshenSpire.NativeLan." + Guid.NewGuid().ToString("N");
        private readonly object _eventGate = new object();
        private readonly Queue<SocketEvent> _events = new Queue<SocketEvent>();
        [Serializable] private sealed class SocketEvent { public int generation; public string type; public string data; }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void AS_NativeLan_Connect(string receiver,string url,int generation);
        [DllImport("__Internal")] private static extern void AS_NativeLan_Send(string receiver,int generation,string message);
        [DllImport("__Internal")] private static extern void AS_NativeLan_Close(string receiver);
#else
        private NativeSocket _native;
#endif
        public void Connect(string url)
        {
            if (!isActiveAndEnabled) throw new InvalidOperationException("Enable the network component before connecting.");
            if (!Uri.TryCreate(url,UriKind.Absolute,out var endpoint) || (endpoint.Scheme != "ws" && endpoint.Scheme != "wss") || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new ArgumentException("Use a ws:// or wss:// companion endpoint.");
            Close(); gameObject.name = _receiverName;
#if UNITY_WEBGL && !UNITY_EDITOR
            AS_NativeLan_Connect(gameObject.name,url,_generation);
#else
            var socket = new NativeSocket(_generation); _native = socket; _ = RunNative(socket,endpoint);
#endif
        }
        public void Send(string message)
        {
            if (!IsConnected) throw new InvalidOperationException("The companion is disconnected.");
            if (message == null || Encoding.UTF8.GetByteCount(message) > MaximumOutboundBytes) throw new ArgumentException("Protocol message exceeds 16 KiB.");
#if UNITY_WEBGL && !UNITY_EDITOR
            AS_NativeLan_Send(gameObject.name,_generation,message);
#else
            if (_native == null || !_native.Enqueue(message)) throw new InvalidOperationException("Connection is closed or its outgoing queue is full.");
#endif
        }
        public void Close()
        {
            _generation++; IsConnected = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            AS_NativeLan_Close(gameObject.name);
#else
            _native?.Close(); _native = null;
#endif
            lock (_eventGate) _events.Clear();
        }
        // Called by the .jslib bridge. Kept public for Unity SendMessage dispatch.
        [UnityEngine.Scripting.Preserve]
        public void OnNativeLanEvent(string payload)
        {
            var message = JsonUtility.FromJson<SocketEvent>(payload);
            if (message != null) Post(message.generation,message.type,message.data);
        }
        private void Post(int generation,string type,string data)
        {
            lock (_eventGate)
            {
                if (generation != _generation) return;
                if (_events.Count >= 128) { _events.Clear(); _events.Enqueue(new SocketEvent { generation = generation, type = "overflow", data = "state_queue_full" }); return; }
                _events.Enqueue(new SocketEvent { generation = generation, type = type, data = data });
            }
        }
        private void Update()
        {
            for (var i = 0; i < 32; i++)
            {
                SocketEvent message; lock (_eventGate) { if (_events.Count == 0) break; message = _events.Dequeue(); }
                if (message.generation != _generation) continue;
                switch (message.type)
                {
                    case "open": IsConnected = true; Opened?.Invoke(); break;
                    case "message": MessageReceived?.Invoke(message.data); break;
                    case "close": IsConnected = false; Closed?.Invoke(message.data); break;
                    case "error": Failed?.Invoke(message.data); break;
                    case "overflow": Close(); Failed?.Invoke(message.data); Closed?.Invoke(message.data); break;
                }
            }
        }
        private void OnDisable() => Close();
        private void OnDestroy() => Close();
#if !UNITY_WEBGL || UNITY_EDITOR
        private sealed class NativeSocket
        {
            public readonly int Generation;
            public readonly ClientWebSocket Socket = new ClientWebSocket();
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly SemaphoreSlim Signal = new SemaphoreSlim(0);
            private readonly object _gate = new object(); private readonly Queue<string> _outgoing = new Queue<string>(); private bool _closed;
            public NativeSocket(int generation) { Generation = generation; Socket.Options.AddSubProtocol(Subprotocol); Socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20); }
            public bool Enqueue(string message) { lock (_gate) { if (_closed || _outgoing.Count >= 16) return false; _outgoing.Enqueue(message); Signal.Release(); return true; } }
            public string Take() { lock (_gate) return _outgoing.Dequeue(); }
            public void Close() { lock (_gate) { if (_closed) return; _closed = true; Cancellation.Cancel(); Socket.Abort(); } }
        }
        private async Task WriteNative(NativeSocket state)
        {
            try { while (true) { await state.Signal.WaitAsync(state.Cancellation.Token).ConfigureAwait(false); var message = state.Take(); await state.Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),WebSocketMessageType.Text,true,state.Cancellation.Token).ConfigureAwait(false); } }
            catch { state.Close(); throw; }
        }
        private async Task RunNative(NativeSocket state,Uri endpoint)
        {
            Task writer = null; bool opened = false;
            try
            {
                await state.Socket.ConnectAsync(endpoint,state.Cancellation.Token).ConfigureAwait(false);
                opened = true; Post(state.Generation,"open",""); writer = WriteNative(state);
                var buffer = new byte[8192];
                while (true)
                {
                    using (var bytes = new MemoryStream())
                    {
                        WebSocketReceiveResult part;
                        do
                        {
                            part = await state.Socket.ReceiveAsync(new ArraySegment<byte>(buffer),state.Cancellation.Token).ConfigureAwait(false);
                            if (part.MessageType == WebSocketMessageType.Close) return;
                            if (part.MessageType != WebSocketMessageType.Text || bytes.Length + part.Count > MaximumInboundBytes) throw new InvalidOperationException("Companion message violates the text/size limit.");
                            bytes.Write(buffer,0,part.Count);
                        } while (!part.EndOfMessage);
                        Post(state.Generation,"message",new UTF8Encoding(false,true).GetString(bytes.ToArray()));
                    }
                }
            }
            catch (Exception error) when (error is WebSocketException || error is OperationCanceledException || error is IOException || error is InvalidOperationException || error is DecoderFallbackException)
            { if (!state.Cancellation.IsCancellationRequested) Post(state.Generation,"error",opened ? "connection_failed" : "companion_unreachable"); }
            finally
            {
                state.Close();
                if (writer != null) try { await writer.ConfigureAwait(false); } catch (Exception error) when (error is WebSocketException || error is OperationCanceledException || error is IOException || error is InvalidOperationException) { }
                state.Socket.Dispose(); state.Signal.Dispose(); state.Cancellation.Dispose(); Post(state.Generation,"close","companion_disconnected");
            }
        }
#endif
    }
}


