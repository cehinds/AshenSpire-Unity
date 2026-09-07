# Original map knowledge checks

The project links the integrated `OriginalMapKnowledge.cs` directly; it contains no
duplicate C# implementation. It runs without Unity, rendering, or a browser.

```powershell
dotnet run --project UnityTests/MapKnowledge/MapKnowledge.csproj
```

To regenerate the frozen reference, pass the exact original checkout explicitly:

```powershell
node UnityTests/MapKnowledge/oracle.mjs C:/repos/AshenSpire-parity-reference
```

The exporter refuses any commit except
`b17a7f4543e1710f49fae8b58880121690a314de`. It directly calls that checkout's
`mapKnowledge`, `nodeReading`, `shrineLane`, and `nearestShrine`. The original
`mapboard.js` edge loop is extracted and executed verbatim with identity
coordinates, then its SVG lines are decoded into edge readings. Source SHA-256
receipts are embedded in `source-reference.json`. Graph fixtures use the original
generator with the imported authored map configurations plus focused hand-written
graphs; this test does not claim to revalidate the map generator.

The corpus covers all fog/reveal/glow combinations, entrance and path prefixes,
current without path, sticky unchosen forks, resolved and unresolved event nodes,
nearest-shrine ties, multiple/duplicate/missing sources, cycles, dangling edges,
repeated path entries, no shrine, null/empty maps, and clipped shrine guidance.
It checks exact node/edge projection, source/path immutability, and monotonic fog.
No input graph or resolved event payload is retained in the returned projection.

## Consumer contract

```csharp
var reading = OriginalMapKnowledge.Project(map, path, currentId,
    fog: true, revealUnknown: ownsSealstonePassive, shrineGlow: true);
```

`reading.Nodes` contains only visible node readings: `Id`, `ShownType`,
`Knowledge` (`placed` or `known`), `Revealed`, `Visited`, `Current`, `ShrineLane`.
`VisibleIds` is in original node enumeration order. `Edges` contains only edges
whose endpoints are visible: `From`, `To`, `Traveled`, `ShrineLane`.
`NearestShrine(map, sources)` returns `Id`, `Path`, `Distance`, or null.

The run session's legal IDs remain authoritative. Neither this projection nor
`ShrineLane` grants travel. Derive the reveal flag from actual owned item passives;
do not identify the relic by localized name. The pinned original solo default is
fog, while its co-op renderer explicitly requests path mode. Geometry, hit areas,
camera, input cancellation, settings persistence and actual rendered acceptance
belong to the board/application integration and are not established by these tests.
