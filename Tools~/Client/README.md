# GameMaster Console Client

Python desktop and terminal applications for sending debug commands to GameMaster Console servers.

## Desktop app

Use Python 3 with Tkinter. Run `python3 -m tkinter` on macOS or Linux, or `python -m tkinter` on Windows, to check that Tkinter opens a test window. The standard Python installers from python.org include Tcl/Tk. For Homebrew Python, install the `python-tk` formula matching its version. On Debian or Ubuntu, install `python3-tk` and `python3-venv`.

Start the app from this directory:

```text
./run_gui.sh           # macOS / Linux
run_gui.bat            # Windows
```

The launcher installs Python dependencies into a local `.venv-gui` environment. Set `GAMEMASTER_PYTHON` to the path of a Python executable with Tkinter if you need to choose a different interpreter before setup.

The app opens its own window with **Console**, **Help**, and **Logs** tabs. It uses `http://localhost:54345` by default. Enter another address under **Server** and click **Connect**, or pass it to the launcher:

```text
./run_gui.sh --server http://localhost:54346
run_gui.bat --server http://localhost:54346
```

You can also run `gamemaster_gui.py` directly with a Python environment that has Tkinter and the dependencies in `requirements.txt`.

The Unity menu **Window → Game Master → Launch GUI** starts the same desktop app in a separate process. The shortcut copies the client files to the project's `Library/GameMasterConsole/Client` directory. If setup fails, check the Unity Console for the launch error.

## Terminal client

### Features

- **SSDP Discovery**: Automatically discovers GameMaster servers on the network using SSDP protocol
- **Server Selection**: Handles single server auto-selection or presents a list for manual selection when multiple servers are found
- **Command Interface**: Simple command-line interface for sending commands to the GameMaster server
- **Case Insensitive**: All commands are case-insensitive
- **Client Commands**: Built-in commands for application control

### Installation

1. Make sure you have Python 3.6+ installed
2. Run the launcher script (creates a local `.venv` and installs dependencies automatically):
   ```bash
   ./run_client.sh        # macOS / Linux
   run_client.bat         # Windows
   ```

### Usage

Run the client via the launcher:
```bash
./run_client.sh        # macOS / Linux
run_client.bat         # Windows
```

Or manually with the virtual environment:
```bash
python3 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python gamemaster_client.py
```

The client tries a local server at `http://localhost:54345` first, then looks for servers over SSDP. Use `--server` to connect to a specific HTTP address or port.

```bash
./run_client.sh --server http://localhost:54346
run_client.bat --server http://localhost:54346
```

The Unity menu **Window → Game Master → Launch Terminal** copies the launcher to the project's `Library/GameMasterConsole/Client` folder and starts it there. To run the scripts yourself, use that folder or a writable checkout of this repository.

#### Application Flow

1. **Server Selection**: The application tries the default local server before searching for GameMaster servers on the network. If you pass `--server`, it connects directly to that URL.
   - If no servers are found, it will continue searching and prompt you to continue or quit
   - If one server is found, it will be automatically selected
   - If multiple servers are found, you'll be presented with a list to choose from

2. **Command Interface**: Once connected, you can send commands to the server
   - Commands follow the format: `COMMAND ARG1 ARG2 ARG3`
   - All commands are case-insensitive
   - Use **↑** and **↓** to recall earlier commands in the current terminal session. Repeated commands appear once, at their latest position in the history.

#### Built-in Client Commands

- `EXIT` - Close the application
- `RESTART` - Retry the configured URL or repeat automatic server selection

#### Server Commands

Any other commands will be sent to the GameMaster server. The exact commands available depend on what's registered on the server side.

- `help` lists registered commands and their descriptions.
- `SetGameSpeed 0.5` runs the game at half speed.

### Example Session

```
✅ Connected to GameMaster server at http://localhost:54345

GM[Unknown@localhost:54345]> SetGameSpeed 0.5
✅ Скорость игры установлена на 0.5x

GM[Unknown@localhost:54345]> exit
👋 Goodbye!
```

### Technical Details

#### SSDP Discovery Protocol

The client uses SSDP (Simple Service Discovery Protocol) to find GameMaster servers:
- Sends M-SEARCH requests to multicast address `239.255.255.250:1900`
- Looks for servers with service type `urn:schemas-armor-guild:service:GameMaster:1`
- Also searches for device type `urn:schemas-armor-guild:device:GameMasterConsole:1`
- Retrieves server descriptions from UPnP device description XML

#### Communication Protocol

Commands are sent to the server via HTTP POST requests to `/command` endpoint:
- Content-Type: `application/json`
- Request format:
  ```json
  {
    "Command": "COMMAND_NAME",
    "Arguments": {
      "0": "arg1",
      "1": "arg2",
      "2": "arg3"
    }
  }
  ```

#### Error Handling

The client handles various error conditions gracefully:
- Network connectivity issues
- Server timeouts
- Invalid server responses
- User interruption (Ctrl+C)

### Requirements

- Python 3.6+
- `requests` library (for HTTP communication)
- Network access to GameMaster server

### Troubleshooting

#### No Servers Found
- For a local server on a custom port, pass its address with `--server`
- Ensure the GameMaster server is running and has SSDP enabled
- Check that both client and server are on the same network
- Verify firewall settings allow multicast traffic on port 1900
- Try running the server discovery multiple times

#### Connection Errors
- Verify the server IP and port are correct
- Check that the GameMaster server is accepting HTTP connections
- Ensure no firewall is blocking the connection

#### Command Failures
- Check that the command name is correct (case doesn't matter)
- Verify the command is registered on the server
- Check the number and format of arguments
