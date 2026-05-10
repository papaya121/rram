using System;
using System.Collections.Generic;
using Mirror;
using RRaM.Core.Cards;
using RRaM.Core.Data;
using RRaM.Core.Match;
using UnityEngine;
using kcp2k;

namespace RRaM.Core.Networking
{
    /// <summary>
    /// Custom Mirror manager for the first two-player prototype.
    /// </summary>
    public sealed class RramNetworkManager : NetworkManager
    {
        [SerializeField] private MatchPrototypeConfig config;
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private GameObject dwarfPrefab;
        [SerializeField] private GameObject serviceRootPrefab;

        private GameObject serviceRootInstance;
        private bool sceneCardPrefabRegistered;

        public void ConfigureAuthoredSetup(
            MatchPrototypeConfig prototypeConfig,
            GameObject playerPrefabReference,
            GameObject characterPrefabReference,
            GameObject dwarfPrefabReference,
            GameObject serviceRootPrefabReference)
        {
            config = prototypeConfig;
            playerPrefab = playerPrefabReference;
            characterPrefab = characterPrefabReference;
            dwarfPrefab = dwarfPrefabReference;
            serviceRootPrefab = serviceRootPrefabReference;
            RefreshSpawnPrefabs();

            if (config != null)
            {
                ApplyConnectionSettings(config.DefaultAddress, config.NetworkPort);
            }
        }

        /// <summary>
        /// Initializes transport settings and runtime prefab registration.
        /// </summary>
        public override void Awake()
        {
            if (config != null)
            {
                config.ApplyCommandLineOverridesFromEnvironment();
                ApplyConnectionSettings(config.DefaultAddress, config.NetworkPort);
            }

            EnsureTransport();
            maxConnections = 2;
            autoCreatePlayer = false;
            dontDestroyOnLoad = true;
            runInBackground = true;
            RefreshSpawnPrefabs();

            if (!HasAuthoredPrefabs())
            {
                Debug.LogError($"[Network] Authored prefab setup is incomplete. Missing: {DescribeMissingPrefabs()}");
            }
            else
            {
                Debug.Log($"[Network] Using authored prefabs player='{playerPrefab.name}' character='{characterPrefab.name}' dwarf='{dwarfPrefab.name}' services='{serviceRootPrefab.name}'");
            }

            base.Awake();
        }

        public override void Start()
        {
            base.Start();
            TryAutoStartDedicatedServer();
        }

        /// <summary>
        /// Spawns the shared service root after the server starts listening.
        /// </summary>
        public override void OnStartServer()
        {
            if (!HasAuthoredPrefabs())
            {
                Debug.LogError($"[Network] Server start blocked. Missing authored prefab setup: {DescribeMissingPrefabs()}");
                return;
            }

            base.OnStartServer();
            SpawnServiceRoot();
            Debug.Log($"[Server] Listening on 0.0.0.0:{GetCurrentPort()}");
        }

        /// <summary>
        /// Clears cached runtime service references on server shutdown.
        /// </summary>
        public override void OnStopServer()
        {
            serviceRootInstance = null;
            base.OnStopServer();
        }

        /// <summary>
        /// Re-registers runtime prefabs on each client session start.
        /// </summary>
        public override void OnStartClient()
        {
            if (!HasAuthoredPrefabs())
            {
                Debug.LogError($"[Network] Client start blocked. Missing authored prefab setup: {DescribeMissingPrefabs()}");
                return;
            }

            base.OnStartClient();
            RegisterSceneCardPrefab();
        }

        /// <summary>
        /// Validates incoming connections before players enter the lobby.
        /// </summary>
        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            if (numPlayers >= maxConnections)
            {
                Debug.LogWarning("[Server] Connection rejected: lobby is full.");
                conn.Disconnect();
                return;
            }

        }

        /// <summary>
        /// Creates and registers the player session object for a new connection.
        /// </summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            int playerSlot = GetNextAvailablePlayerSlot();
            if (playerSlot < 0)
            {
                Debug.LogWarning("[Server] Failed to assign a player slot.");
                conn.Disconnect();
                return;
            }

            GameObject playerObject = CreatePlayerInstance();
            if (playerObject == null)
            {
                Debug.LogError("[Server] Failed to create authored player prefab instance.");
                conn.Disconnect();
                return;
            }

            NetworkPlayerConnection player = playerObject.GetComponent<NetworkPlayerConnection>();
            player.AssignServerState(playerSlot, $"Player {playerSlot + 1}");
            bool added = NetworkServer.AddPlayerForConnection(conn, playerObject);
            if (!added)
            {
                Debug.LogError("[Server] Failed to add player for connection.");
                Destroy(playerObject);
                return;
            }

            MatchManager.Instance?.ServerRegisterPlayer(player);
        }

        /// <summary>
        /// Unregisters the player session when a connection closes.
        /// </summary>
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn.identity != null && conn.identity.TryGetComponent(out NetworkPlayerConnection player))
            {
                MatchManager.Instance?.ServerUnregisterPlayer(player);
            }

            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Logs a successful client connection.
        /// </summary>
        public override void OnClientConnect()
        {
            base.OnClientConnect();

            if (NetworkClient.localPlayer == null)
            {
                NetworkClient.AddPlayer();
            }
        }

        /// <summary>
        /// Logs a client disconnect event.
        /// </summary>
        public override void OnClientDisconnect()
        {
            sceneCardPrefabRegistered = false;
            NetworkPlayerConnection.ResetLocalChatState();
            base.OnClientDisconnect();
        }

        public void ApplyConnectionSettings(string address, ushort port)
        {
            string resolvedAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            networkAddress = resolvedAddress;
            config?.SetDefaultAddress(resolvedAddress);
            config?.SetNetworkPort(port);
            ApplyTransportPort(port);
        }

        public void TryAutoStartDedicatedServer()
        {
            if (NetworkServer.active || NetworkClient.active || !ShouldAutoStartDedicatedServer())
            {
                return;
            }

            ushort port = config != null ? config.NetworkPort : GetCurrentPort();
            ApplyConnectionSettings(config != null ? config.DefaultAddress : networkAddress, port);
            Debug.Log($"[Server] Dedicated mode detected. Starting server on port {port}.");
            StartServer();
        }

        private void SpawnServiceRoot()
        {
            if (serviceRootInstance != null)
            {
                return;
            }

            serviceRootInstance = CreateServiceRootInstance();
            if (serviceRootInstance == null)
            {
                Debug.LogError("[Server] Failed to create authored service root prefab instance.");
                return;
            }

            NetworkServer.Spawn(serviceRootInstance);
            MatchManager serviceMatchManager = serviceRootInstance.GetComponent<MatchManager>();
            serviceMatchManager.ServerBootstrapLobby();
        }

        public GameObject CreateCharacterInstance()
        {
            return CreateInstanceFromAuthoredPrefab(characterPrefab, "Character", "NetworkCharacter");
        }

        public GameObject CreateDwarfInstance()
        {
            return CreateInstanceFromAuthoredPrefab(dwarfPrefab, "Dwarf", "NetworkDwarf");
        }

        private GameObject CreatePlayerInstance()
        {
            return CreateInstanceFromAuthoredPrefab(playerPrefab, "Player", "NetworkPlayer");
        }

        private GameObject CreateServiceRootInstance()
        {
            return CreateInstanceFromAuthoredPrefab(serviceRootPrefab, "ServiceRoot", "NetworkServices");
        }

        private void EnsureTransport()
        {
            if (transport == null)
            {
                transport = GetComponent<KcpTransport>();
                if (transport == null)
                {
                    transport = gameObject.AddComponent<KcpTransport>();
                }
            }

            ApplyTransportPort(config != null ? config.NetworkPort : (ushort)7777);
        }

        private void ApplyTransportPort(ushort port)
        {
            if (transport is KcpTransport kcpTransport)
            {
                kcpTransport.Port = port == 0 ? (ushort)7777 : port;
            }
        }

        private ushort GetCurrentPort()
        {
            return transport is KcpTransport kcpTransport ? kcpTransport.Port : (ushort)7777;
        }

        private int GetNextAvailablePlayerSlot()
        {
            HashSet<int> usedSlots = new();
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection?.identity == null || !connection.identity.TryGetComponent(out NetworkPlayerConnection player))
                {
                    continue;
                }

                usedSlots.Add(player.PlayerSlot);
            }

            for (int slot = 0; slot < maxConnections; slot++)
            {
                if (!usedSlots.Contains(slot))
                {
                    return slot;
                }
            }

            return -1;
        }

        private bool HasAuthoredPrefabs()
        {
            return playerPrefab != null &&
                   characterPrefab != null &&
                   dwarfPrefab != null &&
                   serviceRootPrefab != null;
        }

        private GameObject CreateInstanceFromAuthoredPrefab(GameObject prefab, string prefabLabel, string instanceName)
        {
            if (prefab == null)
            {
                Debug.LogError($"[Network] Missing authored {prefabLabel} prefab on {nameof(RramNetworkManager)}.");
                return null;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = instanceName;
            instance.hideFlags = HideFlags.None;
            instance.SetActive(true);
            return instance;
        }

        private void RefreshSpawnPrefabs()
        {
            List<GameObject> authoredSpawnPrefabs = new();

            if (characterPrefab != null)
            {
                authoredSpawnPrefabs.Add(characterPrefab);
            }

            if (dwarfPrefab != null)
            {
                authoredSpawnPrefabs.Add(dwarfPrefab);
            }

            if (serviceRootPrefab != null)
            {
                authoredSpawnPrefabs.Add(serviceRootPrefab);
            }

            spawnPrefabs.RemoveAll(prefab => prefab == null);
            for (int i = 0; i < authoredSpawnPrefabs.Count; i++)
            {
                if (!spawnPrefabs.Contains(authoredSpawnPrefabs[i]))
                {
                    spawnPrefabs.Add(authoredSpawnPrefabs[i]);
                }
            }

            if (playerPrefab != null && spawnPrefabs.Contains(playerPrefab))
            {
                spawnPrefabs.Remove(playerPrefab);
            }
        }

        private void RegisterSceneCardPrefab()
        {
            if (sceneCardPrefabRegistered)
            {
                return;
            }

            Deck deck = Deck.Instance != null ? Deck.Instance : FindAnyObjectByType<Deck>();
            if (deck == null || deck.CardPrefab == null)
            {
                return;
            }

            GameObject cardPrefab = deck.CardPrefab.gameObject;
            if (!cardPrefab.TryGetComponent(out NetworkIdentity identity))
            {
                Debug.LogWarning("[Network] Card prefab is missing NetworkIdentity and can not be registered.");
                return;
            }

            if (!spawnPrefabs.Contains(cardPrefab))
            {
                spawnPrefabs.Add(cardPrefab);
            }

            if (NetworkClient.prefabs.ContainsKey(identity.assetId))
            {
                sceneCardPrefabRegistered = true;
                return;
            }

            NetworkClient.RegisterPrefab(cardPrefab);
            sceneCardPrefabRegistered = true;
        }

        private string DescribeMissingPrefabs()
        {
            List<string> missing = new();
            if (config == null)
            {
                missing.Add(nameof(config));
            }

            if (playerPrefab == null)
            {
                missing.Add(nameof(playerPrefab));
            }

            if (characterPrefab == null)
            {
                missing.Add(nameof(characterPrefab));
            }

            if (dwarfPrefab == null)
            {
                missing.Add(nameof(dwarfPrefab));
            }

            if (serviceRootPrefab == null)
            {
                missing.Add(nameof(serviceRootPrefab));
            }

            return missing.Count == 0 ? "none" : string.Join(", ", missing);
        }

        private static bool ShouldAutoStartDedicatedServer()
        {
            if (Application.isBatchMode)
            {
                return true;
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "-dedicatedServer", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
