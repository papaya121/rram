using System.Collections.Generic;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Dwarfs
{
    /// <summary>
    /// Spawns dwarfs and advances them along predefined routes on the server.
    /// </summary>
    public sealed class DwarfManager : NetworkBehaviour
    {
        public static DwarfManager Instance { get; private set; }

        [SyncVar] public bool DwarfsSpawned;

        private readonly List<NetworkDwarfPawn> dwarfs = new();
        private readonly Dictionary<uint, List<string>> routesByNetId = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Dwarfs] Duplicate dwarf manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Clears all active dwarfs.
        /// </summary>
        [Server]
        public void ServerResetState()
        {
            for (int i = 0; i < dwarfs.Count; i++)
            {
                if (dwarfs[i] != null)
                {
                    NetworkServer.Destroy(dwarfs[i].gameObject);
                }
            }

            dwarfs.Clear();
            routesByNetId.Clear();
            DwarfsSpawned = false;
        }

        /// <summary>
        /// Handles the transition from setup turns into the dwarf phase.
        /// </summary>
        [Server]
        public void ServerResolveSetupPhaseCompleted()
        {
            Match.MatchContext context = Match.MatchContext.Instance;
            if (context == null || context.Config == null || BoardGraph.Instance == null)
            {
                return;
            }

            if (!DwarfsSpawned)
            {
                ServerSpawnDwarfs();
            }

            if (DwarfsSpawned)
            {
                ServerAdvanceDwarfs(context.Config.DwarfStepPerTurn);
            }
        }

        /// <summary>
        /// Advances dwarfs after a regular alternating turn is completed.
        /// </summary>
        [Server]
        public void ServerHandleTurnCompleted()
        {
            Match.MatchContext context = Match.MatchContext.Instance;
            if (!DwarfsSpawned || context == null || context.Config == null)
            {
                return;
            }

            ServerAdvanceDwarfs(context.Config.DwarfStepPerTurn);
        }

        [Server]
        private void ServerSpawnDwarfs()
        {
            IReadOnlyList<DwarfRouteDefinition> routes = BoardGraph.Instance.DwarfRoutes;
            for (int i = 0; i < routes.Count; i++)
            {
                List<string> fullRoute = routes[i].BuildFullRoute();
                if (fullRoute.Count == 0)
                {
                    continue;
                }

                RramNetworkManager networkManager = NetworkManager.singleton as RramNetworkManager;
                GameObject dwarfObject = networkManager != null
                    ? networkManager.CreateDwarfInstance()
                    : null;
                if (dwarfObject == null)
                {
                    Debug.LogError("[Server] Dwarf spawn aborted. No prefab instance available.");
                    continue;
                }

                NetworkDwarfPawn dwarf = dwarfObject.GetComponent<NetworkDwarfPawn>();
                dwarf.ServerInitialize(routes[i].RouteId, i, fullRoute[0]);
                NetworkServer.Spawn(dwarfObject);
                dwarfs.Add(dwarf);
                routesByNetId[dwarf.netId] = fullRoute;
            }
            DwarfsSpawned = dwarfs.Count > 0;
        }

        [Server]
        private void ServerAdvanceDwarfs(int stepCount)
        {
            for (int i = 0; i < dwarfs.Count; i++)
            {
                NetworkDwarfPawn dwarf = dwarfs[i];
                if (dwarf == null || !routesByNetId.TryGetValue(dwarf.netId, out List<string> route))
                {
                    continue;
                }

                int nextStep = Mathf.Min(dwarf.StepIndex + stepCount, route.Count - 1);
                if (nextStep == dwarf.StepIndex)
                {
                    continue;
                }

                dwarf.ServerAdvanceTo(route[nextStep], nextStep);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
