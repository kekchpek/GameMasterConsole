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
        private const double LogPollSeconds = 1;
        private const double LogRetrySeconds = 3;

        [SerializeField] private string _address = GameMasterGuiClient.DefaultAddress;
        [SerializeField] private GameMasterConsoleTab _activeTab;

        private GameMasterConsoleView _view;
        private GameMasterGuiClient _client;
        private GameMasterGuiLogClient _logClient;
        private IVisualElementScheduledItem _logPoll;
        private GameMasterConsoleHistory _history;
        private IReadOnlyList<GameMasterCommandInfo> _commands = Array.Empty<GameMasterCommandInfo>();
        private IReadOnlyList<string> _suggestions = Array.Empty<string>();
        private string _logSessionId;
        private long _lastLogId;
        private double _nextLogPollAt;

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
            _logClient = new GameMasterGuiLogClient();
            string savedHistory = SessionState.GetString(HistoryKey, string.Empty);
            var snapshot = string.IsNullOrEmpty(savedHistory) ? null : JsonUtility.FromJson<HistorySnapshot>(savedHistory);
            _history = new GameMasterConsoleHistory(snapshot?.Entries);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _logPoll?.Pause();
            _logClient?.Dispose();
            _client?.Dispose();
            _view = null;
        }

        public void CreateGUI()
        {
            _logPoll?.Pause();
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
            _view.ConsoleTab.clicked += () => SelectTab(GameMasterConsoleTab.Console);
            _view.HelpTab.clicked += OpenHelp;
            _view.LogsTab.clicked += () => SelectTab(GameMasterConsoleTab.Logs);
            _view.RefreshHelpButton.clicked += RequestHelp;
            _view.RunButton.clicked += RunCommand;
            _view.SetRecentCommands(_history.Recent, RunRecentCommand);

            ResetLogFeed();
            _logPoll = rootVisualElement.schedule.Execute(PollLogs).Every(200);
            _view.ShowTab(_activeTab);
            if (_activeTab != GameMasterConsoleTab.Logs)
                _logPoll.Pause();
            if (_activeTab == GameMasterConsoleTab.Console)
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
            ConsumeKey(evt);
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

            ConsumeKey(evt);
        }

        private static void ConsumeKey(KeyDownEvent evt)
        {
            evt.StopImmediatePropagation();
#if !UNITY_2023_2_OR_NEWER
            // Older UI Toolkit versions run default actions even after propagation stops.
            evt.PreventDefault();
#endif
        }

        private void Recall(int direction)
        {
            if (_history.TryRecall(direction, _view.CommandInput.value, out string input))
                SetInput(input);
        }

        private void FillInput(string input)
        {
            SelectTab(GameMasterConsoleTab.Console);
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

        private void SelectTab(GameMasterConsoleTab tab)
        {
            _activeTab = tab;
            _view.ShowTab(tab);
            if (tab == GameMasterConsoleTab.Logs)
            {
                if (ReadAddress())
                {
                    _nextLogPollAt = 0;
                    _logPoll.Resume();
                    PollLogs();
                }
                return;
            }

            _logPoll.Pause();
            _logClient.Cancel();
            if (tab == GameMasterConsoleTab.Console)
                _view.CommandInput.Focus();
        }

        private void OpenHelp()
        {
            SelectTab(GameMasterConsoleTab.Help);
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
                if (_activeTab == GameMasterConsoleTab.Console)
                    _view.AppendOutput(error, ConsoleLineKind.Error);
                if (_activeTab == GameMasterConsoleTab.Logs)
                {
                    _logPoll.Pause();
                    _logClient.Cancel();
                    _view.SetLogsStatus(error);
                }
                return false;
            }

            if (!string.Equals(_address, address, StringComparison.OrdinalIgnoreCase))
            {
                ResetLogFeed();
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

            if (_activeTab == GameMasterConsoleTab.Logs)
            {
                _nextLogPollAt = 0;
                _logPoll.Resume();
            }
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

        private void ResetLogFeed()
        {
            _logClient.Cancel();
            _logSessionId = null;
            _lastLogId = 0;
            _nextLogPollAt = 0;
            _view.ResetLogs();
            _view.SetLogsStatus("Waiting for Unity logs…");
        }

        private void PollLogs()
        {
            if (_view == null || _activeTab != GameMasterConsoleTab.Logs || _logClient.IsBusy ||
                EditorApplication.timeSinceStartup < _nextLogPollAt)
                return;

            _logClient.Fetch(_address, _logSessionId, _lastLogId, UpdateLogFeed);
        }

        private void UpdateLogFeed(GameMasterGuiLogResult result)
        {
            if (!result.Success)
            {
                _view.SetLogsStatus(result.Error);
                _nextLogPollAt = result.Unsupported ? double.PositiveInfinity : EditorApplication.timeSinceStartup + LogRetrySeconds;
                return;
            }

            GameMasterLogBatch batch = result.Batch;
            if (!string.Equals(_logSessionId, batch.SessionId, StringComparison.Ordinal))
            {
                _logSessionId = batch.SessionId;
                _lastLogId = 0;
                _view.ResetLogs();
            }

            if (batch.Entries.Count > 0)
            {
                _view.AppendLogs(batch.Entries);
                _lastLogId = batch.Entries[batch.Entries.Count - 1].Id;
            }

            _view.SetLogsStatus(_lastLogId == 0 ? "Waiting for Unity logs…" : "Live");
            _nextLogPollAt = EditorApplication.timeSinceStartup + (batch.HasMore ? 0 : LogPollSeconds);
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
