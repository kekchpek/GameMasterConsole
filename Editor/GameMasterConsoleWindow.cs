using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameMasterConsole.Editor
{
    internal sealed class GameMasterConsoleWindow : EditorWindow
    {
        private const string HistoryKey = "GameMasterConsole.CommandHistory";

        [SerializeField] private string _address = GameMasterGuiClient.DefaultAddress;
        [SerializeField] private bool _showHelp;

        private GameMasterConsoleView _view;
        private GameMasterGuiClient _client;
        private GameMasterConsoleHistory _history;
        private IReadOnlyList<GameMasterCommandInfo> _commands = Array.Empty<GameMasterCommandInfo>();
        private IReadOnlyList<string> _suggestions = Array.Empty<string>();

        internal static void Open()
        {
            var window = GetWindow<GameMasterConsoleWindow>("Game Master Console");
            window.minSize = new Vector2(450, 320);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _client = new GameMasterGuiClient();
            string savedHistory = SessionState.GetString(HistoryKey, string.Empty);
            var snapshot = string.IsNullOrEmpty(savedHistory) ? null : JsonUtility.FromJson<HistorySnapshot>(savedHistory);
            _history = new GameMasterConsoleHistory(snapshot?.Entries);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _client?.Dispose();
            _view = null;
        }

        public void CreateGUI()
        {
            _view = new GameMasterConsoleView(rootVisualElement);
            _view.ServerAddress.SetValueWithoutNotify(_address);
            _view.ServerAddress.RegisterCallback<KeyDownEvent>(OnAddressKeyDown, TrickleDown.TrickleDown);
            _view.CommandInput.RegisterCallback<KeyDownEvent>(OnCommandKeyDown, TrickleDown.TrickleDown);
            _view.CommandInput.RegisterValueChangedCallback(_ =>
            {
                _history.ResetNavigation();
                UpdateSuggestions();
            });
            _view.ConnectButton.clicked += RequestHelp;
            _view.ConsoleTab.clicked += () => SelectTab(showHelp: false);
            _view.HelpTab.clicked += OpenHelp;
            _view.RefreshHelpButton.clicked += RequestHelp;
            _view.RunButton.clicked += RunCommand;
            _view.ShowHelp(_showHelp);
            _view.SetRecentCommands(_history.Recent, RunRecentCommand);
            if (!_showHelp)
                _view.CommandInput.schedule.Execute(_view.CommandInput.Focus);
            rootVisualElement.schedule.Execute(RequestHelp).StartingIn(250);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (_view != null && (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode))
                rootVisualElement.schedule.Execute(RequestHelp).StartingIn(250);
        }

        private void OnAddressKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            evt.StopImmediatePropagation();
            RequestHelp();
        }

        private void OnCommandKeyDown(KeyDownEvent evt)
        {
            if (evt.shiftKey || evt.altKey || evt.ctrlKey || evt.commandKey)
                return;

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    RunCommand();
                    break;
                case KeyCode.UpArrow:
                    Recall(-1);
                    break;
                case KeyCode.DownArrow:
                    Recall(1);
                    break;
                case KeyCode.Tab when _suggestions.Count > 0:
                    FillCommandName(_suggestions[0]);
                    break;
                default:
                    return;
            }

            evt.StopImmediatePropagation();
        }

        private void Recall(int direction)
        {
            if (_history.TryRecall(direction, _view.CommandInput.value, out string input))
                SetInput(input);
        }

        private void FillInput(string input)
        {
            SelectTab(showHelp: false);
            _history.ResetNavigation();
            SetInput(input);
        }

        private void FillCommandName(string name) => FillInput(name + " ");

        private void SetInput(string input)
        {
            _view.CommandInput.SetValueWithoutNotify(input);
            _view.CommandInput.Focus();
            _view.CommandInput.SelectRange(input.Length, input.Length);
            UpdateSuggestions();
        }

        private void UpdateSuggestions()
        {
            _suggestions = GameMasterConsoleHistory.GetSuggestions(_view.CommandInput.value, _commands);
            _view.SetSuggestions(_suggestions, FillCommandName);
        }

        private void SelectTab(bool showHelp)
        {
            _showHelp = showHelp;
            _view.ShowHelp(showHelp);
            if (!showHelp)
                _view.CommandInput.Focus();
        }

        private void OpenHelp()
        {
            SelectTab(showHelp: true);
            if (_commands.Count == 0)
                RequestHelp();
        }

        private bool ReadAddress()
        {
            if (!GameMasterGuiClient.TryNormalizeAddress(_view.ServerAddress.value, out string address))
            {
                const string error = "Enter an HTTP or HTTPS server address, such as http://localhost:54345.";
                _view.SetConnectionStatus("Invalid address", false);
                _view.SetHelp(_commands, error, FillCommandName);
                if (!_showHelp)
                    _view.AppendOutput(error, ConsoleLineKind.Error);
                return false;
            }

            if (!string.Equals(_address, address, StringComparison.OrdinalIgnoreCase))
            {
                _commands = Array.Empty<GameMasterCommandInfo>();
                _view.SetHelp(_commands, "Connect to load command descriptions.", FillCommandName);
                UpdateSuggestions();
            }
            _address = address;
            _view.ServerAddress.SetValueWithoutNotify(address);
            return true;
        }

        private void RequestHelp()
        {
            if (_view == null || _client.IsBusy || !ReadAddress())
                return;

            SetRequestInProgress(true);
            _view.SetConnectionStatus("Connecting…", false);
            _view.SetHelp(_commands, "Loading command descriptions…", FillCommandName);
            _client.Execute(_address, "help", Array.Empty<string>(), result =>
            {
                FinishRequest(result);
                if (result.Success)
                    SetCommandDescriptions(result.Message);
                else
                    _view.SetHelp(_commands, result.Message, FillCommandName);
            });
        }

        private void RunCommand()
        {
            SubmitCommand(_view.CommandInput.value, clearInput: true);
        }

        private void RunRecentCommand(string command)
        {
            SubmitCommand(command, clearInput: false);
        }

        private void SubmitCommand(string input, bool clearInput)
        {
            if (_client.IsBusy)
                return;

            string line = input?.Trim();
            if (string.IsNullOrEmpty(line))
                return;
            if (!GameMasterGuiClient.TryParseCommand(line, out string command, out string[] arguments, out string error))
            {
                _view.AppendOutput(error, ConsoleLineKind.Error);
                return;
            }
            if (!ReadAddress())
                return;

            _history.Record(line);
            SessionState.SetString(HistoryKey, JsonUtility.ToJson(new HistorySnapshot { Entries = _history.Entries.ToArray() }));
            _view.SetRecentCommands(_history.Recent, RunRecentCommand);
            _view.AppendOutput(line, ConsoleLineKind.Command);
            if (clearInput)
                SetInput(string.Empty);
            else
                _view.CommandInput.Focus();
            SetRequestInProgress(true);
            _client.Execute(_address, command, arguments, result =>
            {
                FinishRequest(result);
                var output = new StringBuilder(result.Message);
                foreach (var detail in result.Details)
                    output.Append('\n').Append(detail.Key).Append(": ").Append(detail.Value);
                _view.AppendOutput(output.ToString(), result.Success ? ConsoleLineKind.Result : ConsoleLineKind.Error);

                if (result.Success && string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
                    SetCommandDescriptions(result.Message);
                else if (result.Success && _commands.Count == 0)
                    RequestHelp();
            });
        }

        private void SetCommandDescriptions(string message)
        {
            _commands = GameMasterGuiClient.ParseHelp(message);
            string status = _commands.Count == 0 ? "The server returned no command descriptions." : null;
            _view.SetHelp(_commands, status, FillCommandName);
            UpdateSuggestions();
        }

        private void FinishRequest(GameMasterGuiResult result)
        {
            SetRequestInProgress(false);
            _view.SetConnectionStatus(result.ServerResponded ? "Connected" : "Disconnected", result.ServerResponded);
        }

        private void SetRequestInProgress(bool busy)
        {
            _view.ConnectButton.SetEnabled(!busy);
            _view.RunButton.SetEnabled(!busy);
            _view.RefreshHelpButton.SetEnabled(!busy);
            _view.SetRecentCommandsEnabled(!busy);
        }

        [Serializable]
        private sealed class HistorySnapshot
        {
            public string[] Entries;
        }
    }
}
