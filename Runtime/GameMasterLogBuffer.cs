using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GMConsole
{
    internal sealed class GameMasterLogBuffer
    {
        internal const int MaxPageSize = 200;
        private const int Capacity = 1000;

        private readonly object _gate = new object();
        private readonly Queue<LogEntry> _entries = new Queue<LogEntry>(Capacity);
        private long _lastId;

        internal string SessionId { get; } = Guid.NewGuid().ToString("N");

        internal void Capture(string message, string stackTrace, LogType type)
        {
            lock (_gate)
            {
                if (_entries.Count == Capacity)
                    _entries.Dequeue();

                _entries.Enqueue(new LogEntry(++_lastId, DateTime.UtcNow, type, message, stackTrace));
            }
        }

        internal JObject ReadPage(long after, int limit, string session)
        {
            if (!string.IsNullOrEmpty(session) && !string.Equals(session, SessionId, StringComparison.Ordinal))
                after = 0;

            var page = new List<LogEntry>(limit);
            bool hasMore = false;
            lock (_gate)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Id <= after)
                        continue;

                    if (page.Count == limit)
                    {
                        hasMore = true;
                        break;
                    }

                    page.Add(entry);
                }
            }

            var entries = new JArray();
            foreach (var entry in page)
            {
                entries.Add(new JObject
                {
                    ["Id"] = entry.Id,
                    ["Timestamp"] = entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    ["Type"] = entry.Type.ToString(),
                    ["Message"] = entry.Message,
                    ["StackTrace"] = entry.StackTrace
                });
            }

            return new JObject
            {
                ["SessionId"] = SessionId,
                ["Entries"] = entries,
                ["HasMore"] = hasMore
            };
        }

        private sealed class LogEntry
        {
            internal readonly long Id;
            internal readonly DateTime Timestamp;
            internal readonly LogType Type;
            internal readonly string Message;
            internal readonly string StackTrace;

            internal LogEntry(long id, DateTime timestamp, LogType type, string message, string stackTrace)
            {
                Id = id;
                Timestamp = timestamp;
                Type = type;
                Message = message ?? string.Empty;
                StackTrace = stackTrace ?? string.Empty;
            }
        }
    }
}
