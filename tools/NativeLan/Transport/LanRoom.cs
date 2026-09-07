// LanRoom.cs — authenticated lobby identity and server-authoritative routing.
// Transport secrets never enter game snapshots/public views. One gate serializes
// joins, disconnects and intents; the native run owns gameplay validation/sequence.
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Transport;

public sealed class LanRoom : IDisposable
{
    private sealed class Seat(string id,string name,JObject setup,string token,bool host)
    {
        public readonly string Id = id, Token = token;
        public string Name = name;
        public JObject Setup = setup;
        public readonly bool Host = host;
        public bool Ready;
        public LanConnection? Connection;
    }
    private readonly LanOptions _options;
    private readonly ILanGameFactory _factory;
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly List<Seat> _seats = [];
    private ILanGame? _game;
    private uint _seed = 1;
    private bool _endless;
    private int _nextSeat, _connections;
    private long _revision;
    private bool _storageFault;
    private readonly FileStream? _stateLock;
    public LanRoom(LanOptions options,ILanGameFactory factory)
    {
        _options = options; _factory = factory;
        _stateLock = LanStateStore.Acquire(options.StatePath);
        try {
        var saved = LanStateStore.Load(options.StatePath);
        if (saved != null)
        {
            try
            {
                if ((int?)saved["schemaVersion"] != 1) throw new ArgumentException("Host state schema mismatch.");
                var join = Text(saved["joinToken"],"join_token",128); var host = Text(saved["hostToken"],"host_token",128);
                if (join.Length < 32 || host.Length < 32 || join == host) throw new ArgumentException("Invalid host credentials.");
                _options = options with { JoinToken = join, HostToken = host };
                _seed = (uint)saved["seed"]!; _endless = (bool)saved["endless"]!; _nextSeat = (int)saved["nextSeat"]!; _revision = (long)saved["revision"]!;
                if (saved["game"] is JObject frozenGame) _game = factory.Restore(frozenGame,true);
                foreach (var row in (JArray)saved["seats"]!)
                {
                    var token = Text(row["token"],"seat_token",128); if (token.Length < 32) throw new ArgumentException("Invalid seat credential.");
                    _seats.Add(new Seat(Text(row["id"],"seat_id"),Text(row["name"],"name",32),ValidatePlayerSetup((JObject)row["setup"]!),token,(bool)row["host"]!));
                }
                if (_seats.Count > options.MaximumSeats || _seats.Select(s => s.Id).Distinct().Count() != _seats.Count || _seats.Select(s => s.Token).Distinct().Count() != _seats.Count || _seats.Count(s => s.Host) > 1 || _nextSeat < _seats.Count || _revision < 0) throw new ArgumentException("Invalid saved lobby.");
                if (saved["game"] is JObject game)
                {
                    if (!_game!.View()["party"]!.Select(p => (string)p["id"]!).OrderBy(x => x).SequenceEqual(_seats.Select(s => s.Id).OrderBy(x => x))) throw new ArgumentException("Saved party does not match transport seats.");
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException or ProtocolException or InvalidCastException)
            { throw new InvalidDataException("host_state_invalid: existing save was preserved; explicit backup recovery is required.",error); }
        }
        else Persist();
        } catch { _stateLock?.Dispose(); throw; }
    }
    public void Dispose() => _stateLock?.Dispose();
    // Trusted local CLI only: do not expose these values through HTTP or game views.
    public (string JoinToken,string HostToken) LocalCredentials() => (_options.JoinToken,_options.HostToken);
    private JObject ValidatePlayerSetup(JObject setup) => _game?.ValidatePlayerSetup(setup) ?? _factory.ValidatePlayerSetup(setup);
    private void Persist()
    {
        if (_storageFault) throw new IOException("host_storage_failed");
        try
        {
            LanStateStore.Save(_options.StatePath,new JObject { ["schemaVersion"] = 1, ["joinToken"] = _options.JoinToken, ["hostToken"] = _options.HostToken,
                ["seed"] = _seed, ["endless"] = _endless, ["nextSeat"] = _nextSeat, ["revision"] = _revision, ["game"] = _game?.Snapshot(),
                ["seats"] = new JArray(_seats.Select(s => new JObject { ["id"] = s.Id, ["name"] = s.Name, ["setup"] = s.Setup.DeepClone(), ["token"] = s.Token, ["host"] = s.Host })) });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _storageFault = true; foreach (var seat in _seats) seat.Connection?.Abort(); throw new IOException("host_storage_failed: command acknowledgment withheld; restart from the last committed save.",error); }
    }
    public int ConnectionCount => Volatile.Read(ref _connections);
    internal bool TryConnect() { if (Interlocked.Increment(ref _connections) <= _options.MaximumConnections) return true; Interlocked.Decrement(ref _connections); return false; }
    private static bool TokenEquals(string? supplied,string expected)
    {
        if (supplied == null || supplied.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(expected));
    }
    private static string Text(JToken? value,string field,int maximum=64)
    {
        var text = value?.Type == JTokenType.String ? value.Value<string>() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)) throw new ProtocolException("invalid_" + field);
        return text.Trim();
    }
    private static JObject Envelope(string type,JObject payload,string? requestId=null) => new() { ["v"] = 1, ["type"] = type, ["requestId"] = requestId, ["payload"] = payload };
    private JObject Lobby() => new() { ["revision"] = _revision, ["started"] = _game != null, ["seed"] = _seed, ["endless"] = _endless, ["maximumSeats"] = _options.MaximumSeats,
        ["seats"] = new JArray(_seats.Select(s => new JObject { ["id"] = s.Id, ["name"] = s.Name, ["host"] = s.Host, ["connected"] = s.Connection != null, ["ready"] = s.Ready, ["setup"] = s.Setup.DeepClone() })) };
    private void Broadcast(JArray? events=null,Action? acknowledge=null)
    {
        _revision++; Persist(); acknowledge?.Invoke();
        foreach (var seat in _seats) if (seat.Connection != null)
            seat.Connection.Send(Envelope("state",new JObject { ["revision"] = _revision, ["lobby"] = Lobby(), ["game"] = _game?.View(seat.Id), ["events"] = events ?? new JArray() }));
    }
    internal async Task Process(LanConnection connection,JObject message)
    {
        await _gate.WaitAsync();
        try
        {
            if (_storageFault) throw new ProtocolException("host_storage_failed");
            if ((int?)message["v"] != 1 || message["v"]?.Type != JTokenType.Integer) throw new ProtocolException("protocol_version");
            if (message.Properties().Any(p => !new[] { "v", "type", "requestId", "payload" }.Contains(p.Name))) throw new ProtocolException("unknown_envelope_field");
            var type = Text(message["type"],"type"); var requestId = message["requestId"] == null || message["requestId"]!.Type == JTokenType.Null ? null : Text(message["requestId"],"request_id");
            var payload = message["payload"] as JObject ?? throw new ProtocolException("payload_object_required");
            if (connection.SeatId == null)
            {
                JObject welcome;
                if (type != "hello") throw new ProtocolException("authentication_failed");
                if (payload["resumeToken"]?.Type == JTokenType.String)
                {
                    var seat = _seats.FirstOrDefault(s => TokenEquals((string?)payload["resumeToken"],s.Token)) ?? throw new ProtocolException("authentication_failed");
                    if (seat.Connection != null) throw new ProtocolException("seat_already_connected");
                    _game?.SetConnected(seat.Id,true); seat.Connection = connection; connection.SeatId = seat.Id;
                    welcome = Envelope("welcome",new JObject { ["seatId"] = seat.Id, ["resumeToken"] = seat.Token, ["host"] = seat.Host, ["rejoined"] = true },requestId);
                }
                else
                {
                    if (!TokenEquals((string?)payload["joinToken"],_options.JoinToken)) throw new ProtocolException("authentication_failed");
                    if (_seats.Count >= _options.MaximumSeats) throw new ProtocolException("lobby_full");
                    var host = payload["hostToken"]?.Type == JTokenType.String;
                    if (host && (!TokenEquals((string?)payload["hostToken"],_options.HostToken) || _seats.Any(s => s.Host))) throw new ProtocolException("authentication_failed");
                    var name = Text(payload["name"],"name",32); var setup = ValidatePlayerSetup(payload["setup"] as JObject ?? throw new ProtocolException("setup_required"));
                    var seat = new Seat("p" + ++_nextSeat,name,setup,LanOptions.NewToken(),host) { Connection = connection };
                    _game?.AddMember(new LanMember(seat.Id,seat.Name,(JObject)seat.Setup.DeepClone()));
                    _seats.Add(seat); connection.SeatId = seat.Id;
                    welcome = Envelope("welcome",new JObject { ["seatId"] = seat.Id, ["resumeToken"] = seat.Token, ["host"] = seat.Host, ["rejoined"] = false },requestId);
                }
                Broadcast(acknowledge:() => connection.Send(welcome)); return;
            }
            var current = _seats.FirstOrDefault(s => s.Id == connection.SeatId && s.Connection == connection) ?? throw new ProtocolException("seat_not_owned");
            try
            {
                JObject receipt = new() { ["ok"] = true }; JArray? events = null;
                switch (type)
                {
                    case "pick":
                        RequireLobby(); var selected = _factory.ValidatePlayerSetup(payload["setup"] as JObject ?? throw new ProtocolException("setup_required"));
                        var name = payload["name"] == null ? current.Name : Text(payload["name"],"name",32); current.Setup = selected; current.Name = name; current.Ready = false; break;
                    case "ready":
                        RequireLobby(); if (payload["ready"]?.Type != JTokenType.Boolean) throw new ProtocolException("ready_boolean_required"); current.Ready = (bool)payload["ready"]!; break;
                    case "seed":
                        RequireLobby(); RequireHost(current); if (payload["seed"]?.Type != JTokenType.Integer || (long)payload["seed"]! < 0 || (long)payload["seed"]! > uint.MaxValue) throw new ProtocolException("invalid_seed"); _seed = (uint)payload["seed"]!; break;
                    case "endless":
                        RequireLobby(); RequireHost(current); if (payload["enabled"]?.Type != JTokenType.Boolean) throw new ProtocolException("endless_boolean_required"); _endless = (bool)payload["enabled"]!; break;
                    case "removeSeat":
                        RequireLobby(); RequireHost(current); var forgotten = _seats.FirstOrDefault(s => s.Id == (string?)payload["seatId"] && s.Connection == null && !s.Host) ?? throw new ProtocolException("seat_cannot_be_removed"); _seats.Remove(forgotten); break;
                    case "start":
                        RequireLobby(); RequireHost(current); if (_seats.Count < 2 || _seats.Any(s => !s.Ready || s.Connection == null)) throw new ProtocolException("all_players_must_be_ready");
                        var candidate = _factory.Create(_seed,_endless,_seats.OrderByDescending(s => s.Host).Select(s => new LanMember(s.Id,s.Name,(JObject)s.Setup.DeepClone())).ToArray());
                        receipt = candidate.Execute(current.Id,Sequence(payload),new JObject { ["type"] = "start" });
                        if ((bool?)receipt["ok"] != true) { connection.Send(Envelope("receipt",receipt,requestId)); return; }
                        _game = candidate; events = receipt["events"] as JArray; break;
                    case "intent":
                        if (_game == null) throw new ProtocolException("run_not_started"); var intent = payload["intent"] as JObject ?? throw new ProtocolException("intent_object_required");
                        if (intent.Properties().Any(p => new[] { "memberId", "actorId", "playerId", "seatId" }.Contains(p.Name))) throw new ProtocolException("actor_identity_is_server_owned");
                        receipt = _game.Execute(current.Id,Sequence(payload),(JObject)intent.DeepClone()); events = receipt["events"] as JArray; break;
                    case "resync": break;
                    default: throw new ProtocolException("unknown_message_type");
                }
                Broadcast(events,() => connection.Send(Envelope("receipt",receipt,requestId)));
            }
            catch (Exception error) when (error is ProtocolException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
            { connection.Send(Envelope("error",new JObject { ["code"] = error is ProtocolException ? error.Message : "command_refused", ["message"] = error.Message },requestId)); }
        }
        finally { _gate.Release(); }
    }
    private static long Sequence(JObject payload) => payload["sequence"]?.Type == JTokenType.Integer && (long)payload["sequence"]! >= 1 ? (long)payload["sequence"]! : throw new ProtocolException("positive_sequence_required");
    private void RequireLobby() { if (_game != null) throw new ProtocolException("run_already_started"); }
    private static void RequireHost(Seat seat) { if (!seat.Host) throw new ProtocolException("host_required"); }
    internal async Task Disconnected(LanConnection? connection)
    {
        await _gate.WaitAsync();
        try
        {
            var seat = connection == null ? null : _seats.FirstOrDefault(s => s.Connection == connection);
            if (seat != null) { seat.Connection = null; seat.Ready = false; if (!_storageFault) { _game?.SetConnected(seat.Id,false); Broadcast(); } }
        }
        finally { Interlocked.Decrement(ref _connections); _gate.Release(); }
    }
}




