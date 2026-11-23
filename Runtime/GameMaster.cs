using System;
using UnityEngine;

namespace GMConsole
{
    /// <summary>
    /// Static API for the shared GameMaster command registry and HTTP server.
    /// </summary>
    public static class GameMaster
    {
        private static Lazy<GameMasterServer> _server = new Lazy<GameMasterServer>(CreateServer);

        /// <summary>
        /// Gets or creates the server used by the static API.
        /// </summary>
        public static GameMasterServer Instance => _server.Value;

        /// <summary>
        /// Starts the shared server. Call from the Unity main thread.
        /// </summary>
        public static void StartServer(
            int port = 54345,
            bool allowNetworkAccess = false,
            bool logRequests = true,
            bool enableSsdp = false)
        {
            Instance.StartServer(port, allowNetworkAccess, logRequests, enableSsdp);
        }

        /// <summary>
        /// Stops the shared server and keeps its registered commands for the next start.
        /// </summary>
        public static void StopServer()
        {
            if (_server.IsValueCreated)
                Instance.StopServer();
        }

        public static void RegisterCommand(string command, string description, Action<GMArgs> handler)
        {
            Instance.RegisterCommand(command, description, handler);
        }

        public static bool UnregisterCommand(string command)
        {
            return Instance.UnregisterCommand(command);
        }

        public static bool IsCommandRegistered(string command)
        {
            return Instance.IsCommandRegistered(command);
        }

        public static string[] GetRegisteredCommands()
        {
            return Instance.GetRegisteredCommands();
        }

        private static GameMasterServer CreateServer()
        {
            return new GameMasterServer();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            StopServer();
            _server = new Lazy<GameMasterServer>(CreateServer);
            Application.quitting -= StopServer;
            Application.quitting += StopServer;
        }
    }
}
