# Native LAN tests

Copy this `Tests/` directory to `tools/NativeLan/Tests/`. It contains source only: no invitation codes, resume tokens, saves, receipts or server logs. Requires .NET SDK 8 and Node.js 22 or later (global WebSocket).

From the repository root, run sequentially:

```powershell
dotnet build tools/NativeLan/Companion/AshenSpire.Companion.csproj -c Release
dotnet run --project tools/NativeLan/Tests/Checks/TransportChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/NativeChecks/NativeChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/PersistenceChecks/PersistenceChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/StartingChoicesChecks/StartingChoicesChecks.csproj -c Release
```

All game rules compile directly from the integrated Original domain through the companion project. The transport boundary test alone uses an explicit recording spy. The native suite runs real cards, costs, targeting, late join, encounter rewards and exact retries through real WebSockets; it launches the actual `.jslib` in a Node Emscripten-export harness. This is not Unity browser evidence. The persistence suite starts and abruptly kills only its own companion child processes, then checks exact saved state, rejoin tokens and backup recovery. The creation suite validates original appearance identities and keepsake effects against authored content.

No Unity Web build is required for CI. Tests create a clearly labeled static HTML/WASM fixture solely for HTTP serving checks. Unity Editor compilation, actual IL2CPP builds, browser input tests and device testing remain separate required validation layers.

Tests create private ephemeral credentials and state in the OS temporary directory under `AshenSpire.NativeLan.Tests/<unique-id>`. Do not upload this directory as CI artifacts. Publish only console summaries; it is safe to retain the small `receipt.json`, `native-receipt.json` and `starting-choices.json` summaries after explicit selection. Never upload `host-state*`, before/after host snapshots, server output, or the entire test output folder.

Optional environment variables:

| Variable | Purpose |
| --- | --- |
| `AS_LAN_REPOSITORY_ROOT` | Exact source repository when running outside it. |
| `AS_LAN_CONTENT_ROOT` | Authored Original JSON directory. |
| `AS_LAN_COMPANION_DLL` | Exact already-built companion DLL for restart tests. |
| `AS_LAN_WEB_ROOT` | Real compiled Web folder for additional serving validation. |
| `AS_LAN_TEST_OUTPUT` | Private output directory; caller must provide a unique path. |
| `AS_LAN_BRIDGE_HARNESS` | Alternate `bridge-checks.mjs` path. |
| `AS_LAN_JSLIB` | Exact integrated Unity bridge file. |
| `NATIVE_COMPANION_EXE` | Self-contained packaged companion EXE for restart tests. |
| `NATIVE_COMPANION_CONTENT` | Packaged Content directory paired with that EXE. |

When running these scratch tests before copying, pass `-p:NativeLanSourceRoot=<repo>/tools/NativeLan` and set `AS_LAN_REPOSITORY_ROOT`. Native checks also require `AS_LAN_BRIDGE_HARNESS` to point to this scratch harness. This override changes only project/file locations, never the game rules.

Unity adapter compilation is performed by the actual Unity project build. A lightweight native/WebGL C# compile may additionally reference the installed Editor's CoreModule, JSONSerializeModule and ScriptingModule assemblies. Such a compile is not an IL2CPP/device test.
