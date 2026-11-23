# GameMaster Console

Register debug commands in Unity and run them from a Python console or an HTTP client. The server includes `help` and `SetGameSpeed` and supports optional SSDP discovery on a local network.

## Install in Unity

Requires Unity 6.2 or newer and Git. Unity Package Manager installs Newtonsoft.Json automatically.

Open **Window → Package Manager**, choose **+ → Install package from git URL**, and enter:

```text
https://github.com/kekchpek/GameMasterConsole.git#v0.1.0
```

You can also add this entry under `dependencies` in `Packages/manifest.json`:

```json
"com.kekchpek.gamemaster-console": "https://github.com/kekchpek/GameMasterConsole.git#v0.1.0"
```

If your scripts use an assembly definition, add a reference to the `GameMasterConsole` assembly. If the project already contains a copy of the module under `Assets`, remove that copy when switching to the package.

## Register and run a command

Attach this component to a GameObject in a debug scene and enter Play Mode:

```csharp
using GMConsole;
using UnityEngine;

public sealed class DebugConsoleBootstrap : MonoBehaviour
{
    private void Awake()
    {
        GameMaster.RegisterCommand("ping", "Replies with pong.", args => args.SetResult("pong"));
        GameMaster.StartServer();
    }

    private void OnDestroy()
    {
        GameMaster.StopServer();
        GameMaster.UnregisterCommand("ping");
    }
}
```

The static API creates a shared `GameMasterServer` when you first register a command, query the registry, start the server, or read `GameMaster.Instance`. The listener starts only when you call `StartServer()` from Unity's main thread. `StopServer()` keeps the registered commands for a later restart in the same play session. Unity stops the shared server when leaving Play Mode and resets the registry for the next session.

Use `GMArgs.GetString()` and `GMArgs.GetFloat()` to read arguments, and `SetResult()` to send a response. Command names are case-insensitive. Projects that manage dependencies can still construct a `GameMasterServer` and use `IGameMasterServer` and `IGameMasterCommandRegistry` directly.

## Connect a client

By default, the static server listens on `http://localhost:54345`. With Python 3 installed, choose **Window → Game Master → Launch Console Client** in Unity and enter `ping`. The launcher prepares the Python environment in the project's `Library` folder. The client checks the default local address before looking for servers via SSDP.

To connect to a custom address from a checkout of this repository, run:

```sh
bash 'Tools~/Client/run_client.sh' --server http://localhost:54345
```

On Windows, run `"Tools~\Client\run_client.bat" --server http://localhost:54345`. You can also send a command over HTTP:

```sh
curl http://localhost:54345/command \
  -H 'Content-Type: application/json' \
  -d '{"Command":"ping","Arguments":{}}'
```

To connect from another machine on a trusted network, start the server with `GameMaster.StartServer(allowNetworkAccess: true, enableSsdp: true)`. The HTTP server has no authentication. Use it in development sessions on a trusted network.

See the [Python client guide](Tools~/Client/README.md) for more options.
