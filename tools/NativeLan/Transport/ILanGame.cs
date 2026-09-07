// ILanGame.cs — transport boundary around the real native cooperative run.
// The transport supplies authenticated member IDs separately from untrusted JSON.
// Implementations return public views only; full snapshots belong to host storage.
using Newtonsoft.Json.Linq;
namespace AshenSpire.Transport;

public interface ILanGame
{
    JObject View(string? memberId = null);
    JObject Snapshot();
    JObject Execute(string authenticatedMemberId, long sequence, JObject intent);
    void SetConnected(string memberId, bool connected);
    JObject ValidatePlayerSetup(JObject setup);
    void AddMember(LanMember member);
}
public interface ILanGameFactory
{
    JObject ValidatePlayerSetup(JObject setup);
    ILanGame Create(uint seed, bool endless, IReadOnlyList<LanMember> members);
    ILanGame Restore(JObject snapshot, bool disconnectMembers = true);
}
public sealed record LanMember(string Id, string Name, JObject Setup);

public sealed record LanOptions(string WebRoot, string ListenUrl, string JoinToken, string HostToken)
{
    public int MaximumSeats { get; init; } = 4;
    public int MaximumConnections { get; init; } = 12;
    public int MaximumInboundBytes { get; init; } = 16 * 1024;
    public int MaximumOutboundBytes { get; init; } = 2 * 1024 * 1024;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public string? StatePath { get; init; }
    public static string NewToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

