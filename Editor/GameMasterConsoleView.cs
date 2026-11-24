using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameMasterConsole.Editor
{
    internal enum ConsoleLineKind
    {
        Command,
        Result,
        Error
    }

    internal sealed class GameMasterConsoleView
    {
        private static readonly Color CanvasColor = new Color32(22, 27, 35, 255);
        private static readonly Color PanelColor = new Color32(30, 37, 48, 255);
        private static readonly Color OutputColor = new Color32(16, 22, 30, 255);
        private static readonly Color BorderColor = new Color32(54, 66, 82, 255);
        private static readonly Color ButtonColor = new Color32(43, 54, 70, 255);
        private static readonly Color HoverColor = new Color32(55, 69, 88, 255);
        private static readonly Color TextColor = new Color32(235, 241, 249, 255);
        private static readonly Color SecondaryColor = new Color32(172, 186, 202, 255);
        private static readonly Color AccentColor = new Color32(121, 235, 215, 255);
        private static readonly Color AccentBackground = new Color32(38, 78, 78, 255);
        private static readonly Color ErrorColor = new Color32(255, 162, 148, 255);
        private static readonly Font ConsoleFont = EditorGUIUtility.Load("Fonts/RobotoMono-Regular.ttf") as Font;

        private readonly Label _connectionStatus;
        private readonly VisualElement _consolePane;
        private readonly VisualElement _helpPane;
        private readonly ScrollView _output;
        private readonly VisualElement _recentBar;
        private readonly ScrollView _recentCommands;
        private readonly VisualElement _suggestionBar;
        private readonly ScrollView _suggestions;
        private readonly Label _helpStatus;
        private readonly ScrollView _helpRows;

        internal TextField ServerAddress { get; }
        internal TextField CommandInput { get; }
        internal Button ConnectButton { get; }
        internal Button ConsoleTab { get; }
        internal Button HelpTab { get; }
        internal Button RefreshHelpButton { get; }
        internal Button RunButton { get; }

        internal GameMasterConsoleView(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.backgroundColor = CanvasColor;
            root.style.color = TextColor;
            SetPadding(root, 12, 10);

            var connection = Row();
            connection.style.backgroundColor = PanelColor;
            SetOutline(connection);
            SetPadding(connection, 8, 6);
            var addressLabel = Caption("Server");
            addressLabel.style.marginRight = 8;
            connection.Add(addressLabel);
            ServerAddress = Input("server-address", "Server address");
            connection.Add(ServerAddress);
            ConnectButton = CreateButton("connect-button", "Connect");
            ConnectButton.style.marginLeft = 8;
            connection.Add(ConnectButton);
            _connectionStatus = Caption(string.Empty);
            _connectionStatus.name = "connection-status";
            _connectionStatus.style.marginLeft = 10;
            _connectionStatus.style.marginRight = 2;
            _connectionStatus.style.maxWidth = 120;
            _connectionStatus.style.overflow = Overflow.Hidden;
            _connectionStatus.style.textOverflow = TextOverflow.Ellipsis;
            connection.Add(_connectionStatus);
            root.Add(connection);

            var tabs = Row();
            tabs.style.marginTop = 8;
            tabs.style.marginBottom = 8;
            ConsoleTab = CreateButton("console-tab", "Console");
            HelpTab = CreateButton("help-tab", "Help");
            ConsoleTab.style.minWidth = 79;
            HelpTab.style.minWidth = 65;
            HelpTab.style.marginLeft = 5;
            tabs.Add(ConsoleTab);
            tabs.Add(HelpTab);
            root.Add(tabs);

            _consolePane = new VisualElement { name = "console-pane" };
            _consolePane.style.flexGrow = 1;
            _consolePane.style.minHeight = 0;
            _output = VerticalScroll("command-output");
            _output.style.backgroundColor = OutputColor;
            SetOutline(_output);
            SetPadding(_output.contentContainer, 10, 8);
            _consolePane.Add(_output);

            (_recentBar, _recentCommands) = CommandBar("recent-commands", "Recent");
            (_suggestionBar, _suggestions) = CommandBar("suggested-commands", "Complete");
            _consolePane.Add(_recentBar);
            _consolePane.Add(_suggestionBar);

            var inputRow = Row();
            inputRow.style.marginTop = 8;
            var prompt = Caption(">");
            prompt.style.color = AccentColor;
            prompt.style.marginRight = 8;
            prompt.style.fontSize = 16;
            inputRow.Add(prompt);
            CommandInput = Input("command-input", "Enter a command");
            inputRow.Add(CommandInput);
            RunButton = CreateButton("run-button", "Run", primary: true);
            RunButton.style.marginLeft = 8;
            RunButton.style.minWidth = 56;
            inputRow.Add(RunButton);
            _consolePane.Add(inputRow);

            var shortcuts = Caption("Enter run   ·   Tab complete   ·   ↑ ↓ history");
            shortcuts.style.fontSize = 11;
            shortcuts.style.marginTop = 5;
            shortcuts.style.marginLeft = 15;
            _consolePane.Add(shortcuts);
            root.Add(_consolePane);

            _helpPane = new VisualElement { name = "help-pane" };
            _helpPane.style.flexGrow = 1;
            _helpPane.style.minHeight = 0;
            var helpHeader = Row();
            helpHeader.style.marginBottom = 7;
            _helpStatus = Caption(string.Empty);
            _helpStatus.name = "help-status";
            _helpStatus.style.flexGrow = 1;
            _helpStatus.style.whiteSpace = WhiteSpace.Normal;
            _helpStatus.style.marginRight = 8;
            helpHeader.Add(_helpStatus);
            RefreshHelpButton = CreateButton("refresh-help-button", "Refresh");
            helpHeader.Add(RefreshHelpButton);
            _helpPane.Add(helpHeader);
            _helpRows = VerticalScroll("help-commands");
            SetPadding(_helpRows.contentContainer, 0, 1);
            _helpPane.Add(_helpRows);
            root.Add(_helpPane);

            _recentBar.style.display = DisplayStyle.None;
            _suggestionBar.style.display = DisplayStyle.None;
            ShowHelp(false);
        }

        internal void ShowHelp(bool visible)
        {
            _consolePane.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            _helpPane.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            SetTabSelected(ConsoleTab, !visible);
            SetTabSelected(HelpTab, visible);
        }

        internal void SetConnectionStatus(string status, bool connected)
        {
            _connectionStatus.text = ToDisplayText(status);
            _connectionStatus.tooltip = status;
            _connectionStatus.style.color = connected ? AccentColor : SecondaryColor;
        }

        internal void AppendOutput(string text, ConsoleLineKind kind)
        {
            string line = kind == ConsoleLineKind.Command ? $"> {text}" : text;
            var entry = SelectableText(line);
            entry.style.fontSize = 13;
            entry.style.marginBottom = 5;
            entry.style.paddingLeft = 7;
            entry.style.borderLeftWidth = 2;
            entry.style.borderLeftColor = kind switch
            {
                ConsoleLineKind.Command => AccentBackground,
                ConsoleLineKind.Error => ErrorColor,
                _ => Color.clear
            };
            entry.style.color = kind switch
            {
                ConsoleLineKind.Command => AccentColor,
                ConsoleLineKind.Error => ErrorColor,
                _ => TextColor
            };
            UseConsoleFont(entry);
            _output.Add(entry);
            _output.schedule.Execute(() => _output.ScrollTo(entry));
        }

        internal void SetHelp(IReadOnlyList<GameMasterCommandInfo> commands, string status, Action<string> choose)
        {
            _helpStatus.text = ToDisplayText(status);
            _helpRows.Clear();
            foreach (GameMasterCommandInfo command in commands)
            {
                var row = new VisualElement();
                row.style.backgroundColor = PanelColor;
                row.style.flexShrink = 0;
                row.style.marginBottom = 6;
                SetOutline(row);
                SetPadding(row, 10, 7);

                var commandButton = CreateButton("help-command", command.Name);
                commandButton.style.alignSelf = Align.FlexStart;
                commandButton.style.color = AccentColor;
                commandButton.tooltip = "Use this command";
                commandButton.clicked += () => choose(command.Name);
                UseConsoleFont(commandButton);
                row.Add(commandButton);

                if (!string.IsNullOrWhiteSpace(command.Description))
                {
                    var description = SelectableText(command.Description);
                    description.style.color = SecondaryColor;
                    description.style.fontSize = 13;
                    description.style.marginTop = 5;
                    row.Add(description);
                }

                _helpRows.Add(row);
            }
        }

        internal void SetSuggestions(IReadOnlyList<string> commands, Action<string> choose)
        {
            SetCommandBar(_suggestionBar, _suggestions, commands, choose);
        }

        internal void SetRecentCommands(IReadOnlyList<string> commands, Action<string> choose)
        {
            SetCommandBar(_recentBar, _recentCommands, commands, choose);
        }

        internal void SetRecentCommandsEnabled(bool enabled)
        {
            _recentBar.SetEnabled(enabled);
        }

        private static (VisualElement, ScrollView) CommandBar(string name, string heading)
        {
            var row = Row();
            row.name = name;
            row.style.marginTop = 6;
            row.style.backgroundColor = PanelColor;
            SetOutline(row);
            SetPadding(row, 7, 0);
            var title = Caption(heading);
            title.style.minWidth = 58;
            title.style.marginRight = 4;
            row.Add(title);

            var scroll = new ScrollView(ScrollViewMode.Horizontal);
#if UNITY_2021_3_OR_NEWER
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
#endif
            scroll.style.flexGrow = 1;
            scroll.style.minWidth = 0;
            scroll.style.height = 38;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            scroll.contentContainer.style.alignItems = Align.Center;
            row.Add(scroll);
            return (row, scroll);
        }

        private static void SetCommandBar(VisualElement bar, ScrollView scroll, IReadOnlyList<string> commands, Action<string> choose)
        {
            bar.style.display = commands.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            scroll.Clear();
            scroll.scrollOffset = Vector2.zero;
            foreach (string command in commands)
            {
                var button = CreateButton("command-choice", command);
                button.style.marginRight = 5;
                button.style.maxWidth = 220;
                button.style.overflow = Overflow.Hidden;
                button.style.textOverflow = TextOverflow.Ellipsis;
                button.tooltip = command;
                button.clicked += () => choose(command);
                UseConsoleFont(button);
                scroll.Add(button);
            }
        }

        private static ScrollView VerticalScroll(string name)
        {
            var scroll = new ScrollView { name = name };
#if UNITY_2021_3_OR_NEWER
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#endif
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            return scroll;
        }

        private static TextField Input(string name, string placeholder)
        {
            var field = new TextField { name = name };
#if UNITY_6000_0_OR_NEWER
            field.textEdition.placeholder = placeholder;
            field.textSelection.selectAllOnFocus = false;
#else
            field.tooltip = placeholder;
#if UNITY_2022_3_OR_NEWER
            field.selectAllOnFocus = false;
#endif
#endif
            field.style.flexGrow = 1;
            field.style.minWidth = 0;
            SetPadding(field, 0, 0);
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.marginTop = 0;
            field.style.marginBottom = 0;

            VisualElement input = field.Q(className: TextField.inputUssClassName);
            input.style.backgroundColor = OutputColor;
            input.style.color = TextColor;
            input.style.fontSize = 13;
            input.style.minHeight = 28;
            SetOutline(input);
            SetPadding(input, 7, 4);
            UseConsoleFont(input);
            field.RegisterCallback<FocusInEvent>(_ => SetBorderColor(input, AccentColor));
            field.RegisterCallback<FocusOutEvent>(_ => SetBorderColor(input, BorderColor));
            return field;
        }

        private static Button CreateButton(string name, string text, bool primary = false)
        {
            var button = new Button { name = name, text = ToDisplayText(text) };
#if UNITY_2021_3_OR_NEWER
            button.enableRichText = false;
#endif
            button.style.flexShrink = 0;
            button.style.minHeight = 24;
            button.style.color = primary ? AccentColor : TextColor;
            button.style.backgroundColor = primary ? AccentBackground : ButtonColor;
            button.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            SetPadding(button, 8, 3);
            SetOutline(button);
            if (name != "console-tab" && name != "help-tab")
            {
                button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = HoverColor);
                button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = primary ? AccentBackground : ButtonColor);
            }
            return button;
        }

        private static void SetTabSelected(Button tab, bool selected)
        {
            tab.style.color = selected ? AccentColor : SecondaryColor;
            tab.style.backgroundColor = selected ? AccentBackground : PanelColor;
            tab.style.borderBottomColor = selected ? AccentColor : BorderColor;
            tab.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private static Label Caption(string text)
        {
            var label = new Label(ToDisplayText(text));
#if UNITY_2021_3_OR_NEWER
            label.enableRichText = false;
#endif
            label.style.color = SecondaryColor;
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label SelectableText(string text)
        {
            var label = new Label(ToDisplayText(text));
#if UNITY_2021_3_OR_NEWER
            label.enableRichText = false;
#endif
#if UNITY_2022_3_OR_NEWER
            label.parseEscapeSequences = false;
            label.focusable = true;
            label.tabIndex = -1;
            label.selection.isSelectable = true;
#else
            label.AddManipulator(new ContextualMenuManipulator(evt =>
                evt.menu.AppendAction("Copy", _ => GUIUtility.systemCopyBuffer = text,
                    DropdownMenuAction.Status.Normal)));
#endif
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0;
            return label;
        }

        private static string ToDisplayText(string text)
        {
#if UNITY_2021_3_OR_NEWER
            return text;
#else
            // Older text elements always parse rich text. Break tag openers without changing their visible text.
            return text?.Replace("<", "<\u200B");
#endif
        }

        private static void UseConsoleFont(VisualElement element)
        {
            if (ConsoleFont != null)
                element.style.unityFont = ConsoleFont;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            return row;
        }

        private static void SetPadding(VisualElement element, int horizontal, int vertical)
        {
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
        }

        private static void SetOutline(VisualElement element)
        {
            element.style.borderLeftWidth = 1;
            element.style.borderTopWidth = 1;
            element.style.borderRightWidth = 1;
            element.style.borderBottomWidth = 1;
            element.style.borderTopLeftRadius = 5;
            element.style.borderTopRightRadius = 5;
            element.style.borderBottomLeftRadius = 5;
            element.style.borderBottomRightRadius = 5;
            SetBorderColor(element, BorderColor);
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
        }
    }
}
