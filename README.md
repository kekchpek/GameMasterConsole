# GameMaster Console

Register debug commands in Unity and run them from a standalone Python desktop app or a terminal while the game is running.

## Install

With Git installed, open **Window → Package Manager** in Unity, click **+**, choose the Git URL option, and enter `https://github.com/kekchpek/GameMasterConsole.git`. If your scripts use an assembly definition, reference `GameMasterConsole`.

## Register a command

Save this as `PlayerDebugCommands.cs` and attach it to the player in a development scene.

```csharp
using GMConsole;
using UnityEngine;

public sealed class PlayerDebugCommands : MonoBehaviour
{
    private void Awake()
    {
        GameMaster.RegisterCommand("TeleportToCenter", "Move the player to the origin.", TeleportToCenter);
        GameMaster.StartServer();
    }

    private void OnDestroy()
    {
        GameMaster.UnregisterCommand("TeleportToCenter");
        GameMaster.StopServer();
    }

    private void TeleportToCenter(GMArgs args)
    {
        transform.position = Vector3.zero;
        args.SetResult("Player moved to the origin.");
    }
}
```

`RegisterCommand` takes a name, a description shown by `help`, and a handler. In handlers with arguments, use `GMArgs.TryGetFloat` and `GMArgs.TryGetString` to read values and `SetResult` to return a message. Start and stop the shared server from one component. Other components can register their commands and unregister them when destroyed.

## Run commands

Enter **Play Mode** or start a development build that runs the GameMaster server. The default address is `http://localhost:54345`.

### Desktop app

From a checkout of this repository, open `Tools~/Client` and run the launcher for your platform. On macOS and Linux, run `./run_gui.sh`. On Windows, run `run_gui.bat`. In Unity, **Window → Game Master → Launch GUI** is a shortcut to the same app. It runs in its own window and connects to the game over HTTP. See the [Python client setup](Tools~/Client/README.md) for installation and custom server addresses.

Press **Enter** or click **Run** to execute a command. Use **Help** for the server's command descriptions, **Tab** to complete a command name, and **↑ / ↓** to recall distinct commands. Click a **Recent** command to run it again.

### Terminal

Choose **Window → Game Master → Launch Terminal** in Unity, or run the terminal launcher from `Tools~/Client`. The terminal client tries localhost first, then looks for servers on the local network via SSDP. Enter `help` to list commands and use **↑ / ↓** to recall distinct commands.

Enter `TeleportToCenter` in either client to move the player to the scene origin.

## View Unity logs

Open **Logs** in the GUI to see messages from the connected game. The server keeps the latest 1,000 entries from the time it starts. Search the messages, filter by severity, or select an entry to inspect its stack trace.

## Connect to a build

Register commands and start GameMaster Console from the game's startup code in development or release builds. For a build running on the same computer, connect to `http://localhost:54345`.

To connect to a build running on another computer or an Android or iOS device, start the server with `GameMaster.StartServer(allowNetworkAccess: true)`. Keep the build running on the same local development network and use the device's IP address, for example `http://192.168.1.42:54345`. On Android, set **Internet Access** to **Require** in Player Settings to include the network permission.

Enter the address under **Server** in the desktop app and click **Connect**. Both [Python client launchers](Tools~/Client/README.md) also accept `--server`.
