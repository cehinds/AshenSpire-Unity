# Native map viewport

Run from the Unity repository root:

```powershell
dotnet run --project UnityTests/MapViewport/MapViewport.csproj -- .
```

The checked-in oracle is reproducible from a clean original checkout at `b17a7f4543e1710f49fae8b58880121690a314de`:

```powershell
node UnityTests/MapViewport/export-reference.mjs C:/repos/AshenSpire-parity-reference
```

The exporter refuses another commit or dirty original `src`. It executes the original `mapview.js` geometry helpers and extracts/executes the original `mapboard.js` camera functions, including its restoration predicate. Its small layout adapter supplies numeric viewport dimensions, a measured-title-band fixture, and the resulting content extent. This establishes arithmetic parity; it does not claim browser scrollbar rounding, actual font measurements, Unity layout, screenshots, gestures or physical mobile acceptance.

The corpus covers 27 original generated maps: three seeds, three acts and standard/narrow/tall shapes. Its 3,024 camera cases cover fog/path; entrances, intermediate choices, shrines and bosses; phone/desktop dimensions; Fit and saved ladder settings; matching and stale camera restores. Another 120 cases cover the original tap-radius equations. Additional checks cover actual supplied display scales, finite outputs, zero horizontal extent, node bounds, serialization, immutability, malformed camera state and identity invalidation. Original RNG counters are retained and checked unchanged during oracle projection.

## Integration

`OriginalMapViewport.NodesFromMap(map)` accepts the existing original map object/array node collection. Construct the model with its graph columns, authored tap pixels and actual **CSS/display pixels per Unity panel unit**. The original fixture supplies `0.9`; Unity must supply its actual ratio. The renderer owns any additional minimum hit target, which is an accessibility improvement separate from the original circle geometry.

`Project(Request)` receives bounded panel width/height, act/current identity, authoritative reachable IDs, already-derived visible IDs, entrance IDs, setting, optional saved camera and measured title band. Wait for positive layout dimensions. The projector does not calculate visibility, authorize routes, generate graphs or touch RNG.

`Projection.Content` is the graph-space rectangle represented by the content element. The element's width is exactly `Projection.ContentWidth == Request.Width`; its height is `Projection.ContentHeight`. Horizontal scroll is always zero. `NodeCenter(id, false)` / `NodeBounds(id, false)` return scaled content coordinates for rendering inside the vertical scroller. The default `true` returns viewport coordinates, subtracting `ScrollTop`. Do not subtract scroll twice. Bounds describe original visible circles; enlarged hit controls belong to the renderer.

`Projection.State.ToJson()` emits original camel-case camera fields. `Snapshot.FromJson(token)` returns null for malformed old state. `Projection.CaptureScroll(actualScrollTop)` returns a separately frozen, clamped snapshot with the existing act/node/setting identity. Keep it in memory immediately and persist that same snapshot after any debounce. The projection never writes storage. Fit restoration requires the same viewport dimensions within one panel unit; manual/saved cameras preserve their view across a resize. Changed act, current node or zoom setting invalidates restoration.

For +/- use `StepZoom(currentZoom, direction)` and `Request.ManualZoom`; this recenters at a manual zoom using the original policy. Recenter uses `Request.ForceRecenter = true` without `ManualZoom` to return to Fit. Original off-ladder Fit stepping is retained: an off-ladder zoom steps down to 1 or up to 1.3. Vertical panning alone retains the existing framing mode, matching the original scroller.

`DecisionMiss` and `EntranceMiss` report clipping in panel units; `DecisionFits` and `EntranceEndsFit` use the original half-unit tolerance. `TitleVisible` reports the original entrance preference ladder (title+ends, ends, doors). `DeliveredNodePixels(actualScale)` exposes the actual circle diameter so the UI can report or improve a tap-size shortfall. A legal zoom that cannot show the whole decision remains a reported limitation, never an invisible promise.

The implementation additionally refuses numeric camera inputs that overflow projection arithmetic, malformed node geometry/edge collections, and nonpositive layout dimensions. These are input-boundary safeguards, not source gameplay changes.
