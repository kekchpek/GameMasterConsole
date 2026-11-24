using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.Networking;

namespace GameMasterConsole.Editor
{
    internal sealed class GameMasterGuiClient : IDisposable
    {
        internal const string DefaultAddress = "http://localhost:54345";

        private UnityWebRequest _request;
        private UnityWebRequestAsyncOperation _operation;
        private Action<GameMasterGuiResult> _completed;
        private bool _disposed;

        internal bool IsBusy => _request != null;

        internal bool Execute(string address, string command, IReadOnlyList<string> args,
            Action<GameMasterGuiResult> completed)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameMasterGuiClient));
            if (IsBusy)
                return false;

            if (!TryNormalizeAddress(address, out string normalized))
            {
                completed(new GameMasterGuiResult(false, false, $"Enter a server URL such as {DefaultAddress}."));
                return true;
            }
            if (string.IsNullOrWhiteSpace(command))
            {
                completed(new GameMasterGuiResult(false, false, "Enter a command."));
                return true;
            }

            var arguments = new Dictionary<string, string>();
            int argumentCount = args?.Count ?? 0;
            for (int i = 0; i < argumentCount; i++)
                arguments.Add(i.ToString(CultureInfo.InvariantCulture), args[i]);

            try
            {
                string payload = JsonConvert.SerializeObject(new { Command = command, Arguments = arguments });
                _request = new UnityWebRequest(normalized + "/command", UnityWebRequest.kHttpVerbPOST)
                {
                    timeout = 10,
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                _request.SetRequestHeader("Content-Type", "application/json");
                _operation = _request.SendWebRequest();
                _completed = completed;
                EditorApplication.update += Poll;
            }
            catch (Exception exception)
            {
                ReleaseRequest();
                completed(new GameMasterGuiResult(false, false, $"Could not send the command. {exception.Message}"));
            }
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            _completed = null;
            _request?.Abort();
            ReleaseRequest();
        }

        private void Poll()
        {
            if (_operation == null || !_operation.isDone)
                return;

            Action<GameMasterGuiResult> completed = _completed;
            _completed = null;
            GameMasterGuiResult result;
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
            _request?.Dispose();
            _request = null;
        }

        private static GameMasterGuiResult ReadResult(UnityWebRequest request)
        {
            string body = request.downloadHandler.text;
            bool serverResponded = request.responseCode > 0;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string status = serverResponded ? $"Request failed (HTTP {request.responseCode})." : "Could not reach the server.";
                string detail = string.IsNullOrWhiteSpace(body) ? request.error : body.Trim();
                string error = string.IsNullOrWhiteSpace(detail) ? status : status + "\n" + detail;
                return new GameMasterGuiResult(false, serverResponded, error);
            }

            try
            {
                var response = JsonConvert.DeserializeObject<CommandResponse>(body);
                if (response == null)
                    return new GameMasterGuiResult(false, true, "The server returned an empty command response.");

                string message = response.Message;
                if (string.IsNullOrEmpty(message))
                    message = response.Success ? "Command executed successfully." : "Command failed.";

                var details = new List<KeyValuePair<string, string>>();
                if (response.Data != null)
                {
                    foreach (JProperty property in response.Data.Properties())
                    {
                        if (property.Name is "command" or "arguments" or "timestamp" or "status")
                            continue;

                        string value = property.Value.Type == JTokenType.String
                            ? property.Value.Value<string>()
                            : property.Value.ToString(Formatting.None);
                        details.Add(new KeyValuePair<string, string>(property.Name, value));
                    }
                }
                return new GameMasterGuiResult(response.Success, true, message, details);
            }
            catch (JsonException)
            {
                return new GameMasterGuiResult(false, true, "The server returned an invalid command response.");
            }
        }

        internal static bool TryNormalizeAddress(string address, out string normalized)
        {
            normalized = null;
            string input = address?.Trim();
            if (string.IsNullOrEmpty(input))
                return false;
            if (!input.Contains("://"))
                input = "http://" + input;

            if (!Uri.TryCreate(input, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || uri.Port == 0 || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return false;

            normalized = uri.GetLeftPart(UriPartial.Authority);
            return true;
        }

        internal static bool TryParseCommand(string input, out string command, out string[] arguments, out string error)
        {
            command = null;
            arguments = Array.Empty<string>();
            error = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a command.";
                return false;
            }

            var words = new List<string>();
            var word = new StringBuilder();
            bool started = false;
            char quote = '\0';
            for (int i = 0; i < input.Length; i++)
            {
                char character = input[i];
                if (quote != '\0')
                {
                    if (character == quote)
                        quote = '\0';
                    else if (quote == '"' && character == '\\' && i + 1 < input.Length &&
                        (input[i + 1] == '"' || input[i + 1] == '\\'))
                        word.Append(input[++i]);
                    else
                        word.Append(character);
                }
                else if (char.IsWhiteSpace(character))
                {
                    if (started)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                        started = false;
                    }
                }
                else if (!started && (character == '"' || character == '\''))
                {
                    quote = character;
                    started = true;
                }
                else
                {
                    word.Append(character);
                    started = true;
                }
            }

            if (quote != '\0')
            {
                error = "Close the quote before running the command.";
                return false;
            }
            if (started)
                words.Add(word.ToString());
            if (words.Count == 0 || string.IsNullOrWhiteSpace(words[0]))
            {
                error = "Enter a command.";
                return false;
            }

            command = words[0];
            words.RemoveAt(0);
            arguments = words.ToArray();
            return true;
        }

        internal static IReadOnlyList<GameMasterCommandInfo> ParseHelp(string message)
        {
            var commands = new List<GameMasterCommandInfo>();
            var description = new StringBuilder();
            string name = null;
            using var reader = new StringReader(message ?? string.Empty);
            while (reader.ReadLine() is string line)
            {
                int delimiter = line.IndexOf(" - ", StringComparison.Ordinal);
                if (delimiter > 0 && !char.IsWhiteSpace(line[0]))
                {
                    if (name != null)
                        commands.Add(new GameMasterCommandInfo(name, description.ToString().Trim()));

                    name = line.Substring(0, delimiter).TrimEnd();
                    description.Clear();
                    description.Append(line.Substring(delimiter + 3));
                }
                else if (name != null)
                {
                    description.Append('\n').Append(line);
                }
            }
            if (name != null)
                commands.Add(new GameMasterCommandInfo(name, description.ToString().Trim()));

            commands.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            return commands;
        }

        private sealed class CommandResponse
        {
            [JsonProperty(Required = Required.Always)]
            public bool Success { get; set; }

            public string Message { get; set; }
            public JObject Data { get; set; }
        }
    }

    internal sealed class GameMasterCommandInfo
    {
        internal string Name { get; }
        internal string Description { get; }

        internal GameMasterCommandInfo(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    internal sealed class GameMasterGuiResult
    {
        internal bool Success { get; }
        internal bool ServerResponded { get; }
        internal string Message { get; }
        internal IReadOnlyList<KeyValuePair<string, string>> Details { get; }

        internal GameMasterGuiResult(bool success, bool serverResponded, string message,
            IReadOnlyList<KeyValuePair<string, string>> details = null)
        {
            Success = success;
            ServerResponded = serverResponded;
            Message = message;
            Details = details ?? Array.Empty<KeyValuePair<string, string>>();
        }
    }
}
