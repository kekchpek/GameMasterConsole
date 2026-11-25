using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine.Networking;

namespace GameMasterConsole.Editor
{
    internal sealed class GameMasterGuiLogClient : IDisposable
    {
        private const int PageSize = 200;

        private UnityWebRequest _request;
        private UnityWebRequestAsyncOperation _operation;
        private Action<GameMasterGuiLogResult> _completed;
        private bool _disposed;

        internal bool IsBusy => _request != null;

        internal bool Fetch(string address, string sessionId, long after, Action<GameMasterGuiLogResult> completed)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameMasterGuiLogClient));
            if (IsBusy)
                return false;

            if (!GameMasterGuiClient.TryNormalizeAddress(address, out string normalized))
            {
                completed(new GameMasterGuiLogResult($"Enter a server URL such as {GameMasterGuiClient.DefaultAddress}."));
                return true;
            }

            try
            {
                string url = normalized + "/logs?after=" + Math.Max(0, after).ToString(CultureInfo.InvariantCulture)
                    + "&limit=" + PageSize.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(sessionId))
                    url += "&session=" + Uri.EscapeDataString(sessionId);

                _request = UnityWebRequest.Get(url);
                _request.timeout = 10;
                _operation = _request.SendWebRequest();
                _completed = completed;
                EditorApplication.update += Poll;
            }
            catch (Exception exception)
            {
                ReleaseRequest();
                completed(new GameMasterGuiLogResult($"Could not request Unity logs. {exception.Message}"));
            }
            return true;
        }

        internal void Cancel()
        {
            try
            {
                _request?.Abort();
            }
            finally
            {
                ReleaseRequest();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Cancel();
        }

        private void Poll()
        {
            if (_operation == null || !_operation.isDone)
                return;

            Action<GameMasterGuiLogResult> completed = _completed;
            GameMasterGuiLogResult result;
            try
            {
                result = ReadResult(_request);
            }
            finally
            {
                ReleaseRequest();
            }
            completed(result);
        }

        private void ReleaseRequest()
        {
            EditorApplication.update -= Poll;
            _operation = null;
            _completed = null;
            _request?.Dispose();
            _request = null;
        }

        private static GameMasterGuiLogResult ReadResult(UnityWebRequest request)
        {
            if (request.responseCode == 404)
                return new GameMasterGuiLogResult("This server does not provide Unity logs.", unsupported: true);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.responseCode > 0
                    ? $"Could not load Unity logs (HTTP {request.responseCode})."
                    : "Could not reach the server to load Unity logs.";
                return new GameMasterGuiLogResult(error);
            }

            const string invalidResponse = "The server returned an invalid Unity log response.";
            try
            {
                var batch = JsonConvert.DeserializeObject<GameMasterLogBatch>(request.downloadHandler.text);
                return IsValidBatch(batch)
                    ? new GameMasterGuiLogResult(batch)
                    : new GameMasterGuiLogResult(invalidResponse);
            }
            catch (JsonException)
            {
                return new GameMasterGuiLogResult(invalidResponse);
            }
        }

        private static bool IsValidBatch(GameMasterLogBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.SessionId) || batch.Entries == null ||
                batch.Entries.Count > PageSize || (batch.HasMore && batch.Entries.Count == 0))
                return false;

            long previousId = 0;
            foreach (GameMasterLogEntry entry in batch.Entries)
            {
                if (entry == null || entry.Id <= previousId || entry.Message == null)
                    return false;
                previousId = entry.Id;
            }
            return true;
        }
    }

    internal sealed class GameMasterGuiLogResult
    {
        internal bool Success => Batch != null;
        internal bool Unsupported { get; }
        internal string Error { get; }
        internal GameMasterLogBatch Batch { get; }

        internal GameMasterGuiLogResult(GameMasterLogBatch batch)
        {
            Batch = batch;
        }

        internal GameMasterGuiLogResult(string error, bool unsupported = false)
        {
            Error = error;
            Unsupported = unsupported;
        }
    }

    internal sealed class GameMasterLogBatch
    {
        public string SessionId { get; set; }
        public List<GameMasterLogEntry> Entries { get; set; }
        public bool HasMore { get; set; }
    }

    internal sealed class GameMasterLogEntry
    {
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }
}
