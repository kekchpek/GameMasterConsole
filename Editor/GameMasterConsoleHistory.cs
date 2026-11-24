using System;
using System.Collections.Generic;

namespace GameMasterConsole.Editor
{
    internal sealed class GameMasterConsoleHistory
    {
        private const int RecentLimit = 10;
        private const int SuggestionLimit = 6;

        private readonly List<string> _entries = new();
        private readonly List<string> _recent = new();
        private int _historyIndex;
        private string _draft = string.Empty;

        internal GameMasterConsoleHistory(IEnumerable<string> entries = null)
        {
            if (entries == null)
                return;

            foreach (string entry in entries)
                Record(entry);
        }

        internal IReadOnlyList<string> Entries => _entries;
        internal IReadOnlyList<string> Recent => _recent;

        internal void Record(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            _entries.Remove(command);
            _entries.Add(command);
            _recent.Remove(command);
            _recent.Insert(0, command);
            if (_recent.Count > RecentLimit)
                _recent.RemoveAt(RecentLimit);

            ResetNavigation();
        }

        internal bool TryRecall(int direction, string currentInput, out string input)
        {
            input = currentInput ?? string.Empty;
            if (direction != -1 && direction != 1)
                return false;

            int nextIndex = _historyIndex + direction;
            if (nextIndex < 0 || nextIndex > _entries.Count)
                return false;

            if (_historyIndex == _entries.Count)
                _draft = input;

            _historyIndex = nextIndex;
            input = _historyIndex == _entries.Count ? _draft : _entries[_historyIndex];
            return true;
        }

        internal void ResetNavigation()
        {
            _historyIndex = _entries.Count;
            _draft = string.Empty;
        }

        internal static IReadOnlyList<string> GetSuggestions(
            string input, IReadOnlyList<GameMasterCommandInfo> commands)
        {
            if (string.IsNullOrWhiteSpace(input) || commands == null || commands.Count == 0)
                return Array.Empty<string>();

            string prefix = input.TrimStart();
            foreach (char character in prefix)
            {
                if (char.IsWhiteSpace(character))
                    return Array.Empty<string>();
            }

            var suggestions = new List<string>(SuggestionLimit);
            foreach (GameMasterCommandInfo command in commands)
            {
                if (!command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                suggestions.Add(command.Name);
                if (suggestions.Count == SuggestionLimit)
                    break;
            }

            return suggestions;
        }
    }
}
