using RRaM.Core.Board;
using RRaM.Core.CameraControl;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using RRaM.Core.Data;
using RRaM.Core.Dice;
using RRaM.Core.Dwarfs;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Match
{
    /// <summary>
    /// Stores shared scene references for prototype services.
    /// </summary>
    public sealed class MatchContext : MonoBehaviour
    {
        public static MatchContext Instance { get; private set; }

        [Header("Configuration")]
        [Tooltip("Shared editable match configuration asset.")]
        [field: SerializeField] public MatchPrototypeConfig Config { get; private set; }

        [Header("Networking")]
        [Tooltip("Scene network manager used to host or join the match.")]
        [field: SerializeField] public RramNetworkManager NetworkManager { get; private set; }

        [Header("Runtime Services")]
        [Tooltip("Match state coordinator spawned for the current session.")]
        [field: SerializeField] public MatchManager MatchManager { get; private set; }

        [Tooltip("Turn flow controller for the active session.")]
        [field: SerializeField] public TurnManager TurnManager { get; private set; }

        [Tooltip("Dice state controller for the active session.")]
        [field: SerializeField] public DiceManager DiceManager { get; private set; }

        [Header("Board")]
        [Tooltip("Primary board graph used for map positions and topology.")]
        [field: SerializeField] public BoardGraph BoardGraph { get; private set; }

        [Tooltip("Path validator used for legal board movement checks.")]
        [field: SerializeField] public BoardPathValidator BoardPathValidator { get; private set; }

        [Header("Gameplay Services")]
        [Tooltip("Character roster and movement service.")]
        [field: SerializeField] public CharacterManager CharacterManager { get; private set; }

        [Tooltip("Card deck and hand service.")]
        [field: SerializeField] public CardManager CardManager { get; private set; }

        [Tooltip("Dwarf phase manager.")]
        [field: SerializeField] public DwarfManager DwarfManager { get; private set; }

        [Header("Camera")]
        [Tooltip("Local camera controller used by the Canvas buttons.")]
        [SerializeField] private PrototypeCameraController cameraController;

        /// <summary>
        /// Assigns all runtime service references created by the bootstrap.
        /// </summary>
        public void Configure(
            MatchPrototypeConfig config,
            RramNetworkManager networkManager,
            MatchManager matchManager,
            TurnManager turnManager,
            DiceManager diceManager,
            BoardGraph boardGraph,
            BoardPathValidator boardPathValidator,
            CharacterManager characterManager,
            CardManager cardManager,
            DwarfManager dwarfManager)
        {
            Config = config;
            NetworkManager = networkManager;
            MatchManager = matchManager;
            TurnManager = turnManager;
            DiceManager = diceManager;
            BoardGraph = boardGraph;
            BoardPathValidator = boardPathValidator;
            CharacterManager = characterManager;
            CardManager = cardManager;
            DwarfManager = dwarfManager;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            cameraController ??= GetComponent<PrototypeCameraController>();
            cameraController ??= gameObject.AddComponent<PrototypeCameraController>();
        }

        private void OnValidate()
        {
            cameraController ??= GetComponent<PrototypeCameraController>();
#if UNITY_EDITOR
            if (cameraController == null && !Application.isPlaying)
            {
                cameraController = gameObject.AddComponent<PrototypeCameraController>();
            }
#endif
        }

        public void FocusCameraOnCharacters()
        {
            Debug.Log($"[MatchContext] FocusCameraOnCharacters called on '{name}'. cameraController={(cameraController != null ? cameraController.name : "null")}");
            cameraController?.FocusCharacters();
        }

        public void FocusCameraOnMap()
        {
            Debug.Log($"[MatchContext] FocusCameraOnMap called on '{name}'. cameraController={(cameraController != null ? cameraController.name : "null")}");
            cameraController?.FocusMap();
        }
    }
}
