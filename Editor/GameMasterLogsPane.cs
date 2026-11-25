using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameMasterConsole.Editor
{
    internal sealed partial class GameMasterConsoleView
    {
        private sealed class LogsPane
        {
            private const int EntryLimit = 1000;
            private const int RowHeight = 25;
            private static readonly Color WarningColor = new Color32(255, 207, 132, 255);

            private readonly List<GameMasterLogEntry> _entries = new List<GameMasterLogEntry>();
            private readonly List<GameMasterLogEntry> _visible = new List<GameMasterLogEntry>();
            private readonly List<Button> _filters = new List<Button>(4);
            private readonly Label _status;
            private readonly Toggle _autoScroll;
            private readonly ListView _list;
            private readonly ScrollView _details;

            private GameMasterLogEntry _selected;
            private string _severity;
            private string _search = string.Empty;

            internal VisualElement Root { get; }

            internal LogsPane()
            {
                Root = new VisualElement { name = "logs-pane" };
                Root.style.flexGrow = 1;
                Root.style.minHeight = 0;

                var filters = Row();
                filters.style.marginBottom = 6;
                SetTabSelected(AddFilter(filters, "all", "All", null), true);
                AddFilter(filters, "log", "Log", "Log");
                AddFilter(filters, "warning", "Warning", "Warning");
                AddFilter(filters, "error", "Error", "Error");
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                filters.Add(spacer);
                var clear = CreateButton("logs-clear", "Clear");
                clear.tooltip = "Clear the log entries shown in this window";
                clear.clicked += Clear;
                filters.Add(clear);
                Root.Add(filters);

                var searchRow = Row();
                searchRow.style.marginBottom = 6;
                var searchLabel = Caption("Search");
                searchLabel.style.marginRight = 7;
                searchRow.Add(searchLabel);
                var search = Input("logs-search", "Search log text and stack traces");
                search.RegisterValueChangedCallback(evt =>
                {
                    _search = evt.newValue?.Trim() ?? string.Empty;
                    UpdateList();
                });
                searchRow.Add(search);
                _autoScroll = new Toggle("Auto-scroll") { name = "logs-auto-scroll", value = true };
                _autoScroll.style.marginLeft = 9;
                _autoScroll.style.flexShrink = 0;
                _autoScroll.labelElement.style.color = SecondaryColor;
                _autoScroll.labelElement.style.minWidth = 0;
                _autoScroll.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        ScrollToLatest();
                });
                searchRow.Add(_autoScroll);
                Root.Add(searchRow);

                _status = Caption(string.Empty);
                _status.name = "logs-status";
                _status.style.whiteSpace = WhiteSpace.Normal;
                _status.style.marginBottom = 5;
                _status.style.display = DisplayStyle.None;
                Root.Add(_status);

                _list = new ListView
                {
                    name = "logs-list",
                    itemsSource = _visible,
                    makeItem = () => new LogRow(),
                    bindItem = (item, index) => ((LogRow)item).Bind(_visible[index]),
                    selectionType = SelectionType.Single
                };
#if UNITY_2021_3_OR_NEWER
                _list.fixedItemHeight = RowHeight;
#else
                _list.itemHeight = RowHeight;
#endif
#if UNITY_2022_3_OR_NEWER
                _list.selectionChanged += SelectEntry;
#else
                _list.onSelectionChange += SelectEntry;
#endif
                _list.style.flexGrow = 1;
                _list.style.minHeight = 0;
                _list.style.backgroundColor = OutputColor;
                SetOutline(_list);
                Root.Add(_list);

                _details = VerticalScroll("logs-details");
                _details.style.display = DisplayStyle.None;
                _details.style.flexGrow = 0;
                _details.style.flexShrink = 0;
                _details.style.height = Length.Percent(36);
                _details.style.minHeight = 72;
                _details.style.maxHeight = 176;
                _details.style.marginTop = 6;
                _details.style.backgroundColor = PanelColor;
                SetOutline(_details);
                SetPadding(_details.contentContainer, 8, 6);
                Root.Add(_details);
            }

            internal void Append(IReadOnlyList<GameMasterLogEntry> entries)
            {
                if (entries.Count == 0)
                    return;

                _entries.AddRange(entries);
                if (_entries.Count > EntryLimit)
                    _entries.RemoveRange(0, _entries.Count - EntryLimit);
                UpdateList();
            }

            internal void Clear()
            {
                _entries.Clear();
                UpdateList();
            }

            internal void SetStatus(string status)
            {
                _status.text = ToDisplayText(status);
                _status.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            private Button AddFilter(VisualElement filters, string name, string title, string severity)
            {
                var button = CreateButton("logs-filter-" + name, title, selectable: true);
                button.style.marginRight = 5;
                SetTabSelected(button, false);
                button.clicked += () =>
                {
                    _severity = severity;
                    foreach (var filter in _filters)
                        SetTabSelected(filter, filter == button);
                    UpdateList();
                };
                _filters.Add(button);
                filters.Add(button);
                return button;
            }

            private void UpdateList()
            {
                var selected = _selected;
                _list.SetSelectionWithoutNotify(Array.Empty<int>());
                _visible.Clear();
                foreach (var entry in _entries)
                {
                    if (MatchesFilter(entry))
                        _visible.Add(entry);
                }

#if UNITY_2021_3_OR_NEWER
                _list.RefreshItems();
#else
                _list.Refresh();
#endif
                if (selected != null)
                {
                    int index = _visible.IndexOf(selected);
                    if (index >= 0)
                        _list.SetSelectionWithoutNotify(new[] { index });
                    else
                        ShowDetails(null);
                }
                if (_autoScroll.value)
                    ScrollToLatest();
            }

            private bool MatchesFilter(GameMasterLogEntry entry)
            {
                bool severityMatches = _severity == null ||
                    (_severity == "Error" ? IsError(entry.Type) : entry.Type == _severity);
                return severityMatches && (_search.Length == 0 ||
                    Contains(entry.Message, _search) || Contains(entry.StackTrace, _search));
            }

            private static bool Contains(string value, string query)
            {
                return value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool IsError(string type)
            {
                return type == "Error" || type == "Exception" || type == "Assert";
            }

            private void ScrollToLatest()
            {
                _list.schedule.Execute(() =>
                {
                    if (_autoScroll.value && _visible.Count != 0)
                        _list.ScrollToItem(_visible.Count - 1);
                });
            }

            private void SelectEntry(IEnumerable<object> selection)
            {
                foreach (var item in selection)
                {
                    ShowDetails(item as GameMasterLogEntry);
                    return;
                }

                ShowDetails(null);
            }

            private void ShowDetails(GameMasterLogEntry entry)
            {
                _selected = entry;
                _details.Clear();
                _details.style.display = entry == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (entry == null)
                    return;

                var message = SelectableText(entry.Message);
                message.name = "logs-message";
                message.style.color = TextColor;
                UseConsoleFont(message);
                _details.Add(message);

                var heading = Caption("Stack trace");
                heading.style.marginTop = 6;
                _details.Add(heading);
                var stack = SelectableText(string.IsNullOrEmpty(entry.StackTrace) ? "No stack trace." : entry.StackTrace);
                stack.name = "logs-stack-trace";
                stack.style.color = SecondaryColor;
                UseConsoleFont(stack);
                _details.Add(stack);
                _details.scrollOffset = Vector2.zero;
            }

            private sealed class LogRow : VisualElement
            {
                private readonly Label _time;
                private readonly Label _type;
                private readonly Label _message;

                internal LogRow()
                {
                    style.flexDirection = FlexDirection.Row;
                    style.alignItems = Align.Center;
                    SetPadding(this, 6, 0);
                    _time = Column("logs-entry-timestamp");
                    _time.style.width = 64;
                    _time.style.color = SecondaryColor;
                    _type = Column("logs-entry-type");
                    _type.style.width = 76;
                    _type.style.unityFontStyleAndWeight = FontStyle.Bold;
                    _message = Column("logs-entry-message");
                    _message.style.minWidth = 0;
                    _message.style.flexGrow = 1;
                    _message.style.flexShrink = 1;
                    _message.style.color = TextColor;
                    Add(_time);
                    Add(_type);
                    Add(_message);
                }

                internal void Bind(GameMasterLogEntry entry)
                {
                    _time.text = ToDisplayText(FormatTime(entry.Timestamp));
                    _time.tooltip = entry.Timestamp;
                    _type.text = ToDisplayText(entry.Type);
                    _type.style.color = IsError(entry.Type) ? ErrorColor :
                        entry.Type == "Warning" ? WarningColor : AccentColor;
                    _message.text = ToDisplayText(FirstLine(entry.Message));
                }

                private static Label Column(string name)
                {
                    var label = new Label { name = name, pickingMode = PickingMode.Ignore };
#if UNITY_2021_3_OR_NEWER
                    label.enableRichText = false;
#endif
#if UNITY_2022_3_OR_NEWER
                    label.parseEscapeSequences = false;
#endif
                    label.style.whiteSpace = WhiteSpace.NoWrap;
                    label.style.textOverflow = TextOverflow.Ellipsis;
                    label.style.overflow = Overflow.Hidden;
                    label.style.flexShrink = 0;
                    UseConsoleFont(label);
                    return label;
                }

                private static string FormatTime(string timestamp)
                {
                    return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var time)
                        ? time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                        : timestamp;
                }

                private static string FirstLine(string message)
                {
                    if (string.IsNullOrEmpty(message))
                        return string.Empty;

                    int length = message.IndexOfAny(NewLineCharacters);
                    return length < 0 ? message : message.Substring(0, length);
                }

                private static readonly char[] NewLineCharacters = { '\r', '\n' };
            }
        }
    }
}
