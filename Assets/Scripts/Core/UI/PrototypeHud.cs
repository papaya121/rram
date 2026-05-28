using System.Collections.Generic;
using System.Linq;
using Mirror;
using RRaM.Core.Cards;
using RRaM.Core.Board;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Match;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.UI
{
    /// <summary>
    /// Simple IMGUI HUD for launching and driving the prototype.
    /// </summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        private enum ConnectionTarget
        {
            Localhost = 0,
            RemoteServer = 1
        }

        private const float ReferenceScreenWidth = 1600f;
        private const float ReferenceScreenHeight = 900f;
        private const float MinGuiScale = 0.85f;
        private const float MaxGuiScale = 1.2f;
        private const float PanelWidth = 280f;
        private const float PanelPadding = 16f;
        private const float PanelInnerPadding = 20f;
        private const float ButtonWidthFactor = 0.82f;
        private const float ContentSafetyPadding = 10f;
        private const float DiceOverlayWidth = 700f;
        private const float DiceOverlayHeight = 132f;
        private const float ChatLogHeight = 130f;
        private const string PortPrefKey = "RRaM.Network.Port";
        private const string FixedRemoteAddress = "45.144.176.193";
        private const string ChatInputControlName = "PrototypeHud.ChatInput";
        private const float RollDicePromptPendingTimeout = 2f;

        [SerializeField] private GameObject rollDicePromptObject;
        private ConnectionTarget connectionTarget = ConnectionTarget.RemoteServer;
        private string port = "7777";
        private string chatInput = string.Empty;
        private Vector2 hudScrollPosition;
        private Vector2 chatScrollPosition;
        private float currentPanelWidth;
        private GUIStyle wrappedLabelStyle;
        private GUIStyle wrappedButtonStyle;
        private GUIStyle verticalScrollbarStyle;
        private NetworkPlayerConnection[] cachedPlayers = System.Array.Empty<NetworkPlayerConnection>();
        private float nextPlayerRefreshTime;
        private float nextDebugRefreshTime;
        private int cachedCharacterCount;
        private int cachedDwarfCount;
        private int lastRenderedChatCount;
        private bool isDebugVisible;
        private bool rollDiceRequestPending;
        private float rollDiceRequestPendingUntil;

        private void Start()
        {
            if (MatchContext.Instance != null && MatchContext.Instance.Config != null)
            {
                port = MatchContext.Instance.Config.NetworkPort.ToString();
            }

            port = PlayerPrefs.GetString(PortPrefKey, port);
            CardSelectionPanel.EnsureInitialized();
            UpdateRollDicePromptVisibility();
        }

        private void Update()
        {
            UpdateRollDicePromptVisibility();
        }

        private void OnGUI()
        {
            RefreshCachedHudData();
            float guiScale = CalculateGuiScale();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * guiScale);

            float scaledScreenWidth = Screen.width / guiScale;
            float scaledScreenHeight = Screen.height / guiScale;
            float panelWidth = Mathf.Min(PanelWidth, scaledScreenWidth - (PanelPadding * 2f));
            currentPanelWidth = panelWidth;
            UpdateGuiStyles();
            GUILayout.BeginArea(
                new Rect(
                    PanelPadding,
                    PanelPadding,
                    panelWidth,
                    scaledScreenHeight - (PanelPadding * 2f)),
                GUI.skin.box);
            hudScrollPosition = GUILayout.BeginScrollView(hudScrollPosition, false, false, GUIStyle.none, verticalScrollbarStyle);
            GUILayout.BeginVertical(GUILayout.Width(GetContentWidth()));
            DrawConnectionBlock();
            DrawLobbyBlock();
            DrawGameplayBlock();
            DrawChatBlock();
            DrawGameplayInfoToggle();
            DrawGameplayInfoBlock();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
            DrawCenterDiceOverlay();
        }

        private void DrawGameplayInfoToggle()
        {
            GUILayout.Space(10f);
            string buttonText = isDebugVisible ? "Скрыть debug" : "Показать debug";
            if (DrawCenteredButton(buttonText))
            {
                isDebugVisible = !isDebugVisible;
            }
        }

        private static float CalculateGuiScale()
        {
            float widthScale = Screen.width / ReferenceScreenWidth;
            float heightScale = Screen.height / ReferenceScreenHeight;
            float automaticScale = Mathf.Min(widthScale, heightScale);
            return Mathf.Clamp(automaticScale, MinGuiScale, MaxGuiScale);
        }

        private void RefreshCachedHudData()
        {
            float now = Time.unscaledTime;
            if (now >= nextPlayerRefreshTime)
            {
                cachedPlayers = FindObjectsByType<NetworkPlayerConnection>()
                    .OrderBy(player => player.PlayerSlot)
                    .ToArray();
                nextPlayerRefreshTime = now + 0.5f;
            }

            if (now >= nextDebugRefreshTime)
            {
                cachedCharacterCount = FindObjectsByType<NetworkCharacterPawn>().Length;
                cachedDwarfCount = FindObjectsByType<Dwarfs.NetworkDwarfPawn>().Length;
                nextDebugRefreshTime = now + 0.5f;
            }
        }

        private void DrawConnectionBlock()
        {
            DrawWrappedLabel(!NetworkClient.active && !NetworkServer.active ? "Подключение" : "Сеть");
            if (!NetworkClient.active && !NetworkServer.active)
            {
                connectionTarget = (ConnectionTarget)GUILayout.Toolbar((int)connectionTarget, new[] { "Localhost", "VPS" }, GUILayout.Width(GetButtonWidth()));
                if (connectionTarget == ConnectionTarget.RemoteServer)
                {
                    DrawWrappedLabel($"Адрес: {FixedRemoteAddress}");
                }
                else
                {
                    DrawWrappedLabel("Адрес: localhost");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Порт", GUILayout.Width(70f));
                port = GUILayout.TextField(port, GUILayout.Width(GetContentWidth() - 74f));
                GUILayout.EndHorizontal();

                bool allowHostStart = connectionTarget == ConnectionTarget.Localhost;
                GUI.enabled = allowHostStart;
                if (DrawCenteredButton("Запустить Host"))
                {
                    ApplyConnectionSettings();
                    NetworkManager.singleton.StartHost();
                }

                GUI.enabled = true;
                if (DrawCenteredButton("Подключить Client"))
                {
                    ApplyConnectionSettings();
                    NetworkManager.singleton.StartClient();
                }

                if (!allowHostStart)
                {
                    DrawWrappedLabel("В режиме VPS доступно только подключение к удаленному серверу.");
                }

                if (!ushort.TryParse(port, out _) || port == "0")
                {
                    DrawWrappedLabel("Некорректный порт. Будет использован 7777.");
                }

                return;
            }

            DrawWrappedLabel($"Сервер: {(NetworkServer.active ? "включен" : "выключен")}");
            DrawWrappedLabel($"Клиент: {(NetworkClient.active ? "подключен" : "не подключен")}");
            DrawWrappedLabel($"Точка: {ResolveSelectedAddress()}:{ResolveSelectedPort()}");

            if (DrawCenteredButton("Остановить"))
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                {
                    NetworkManager.singleton.StopHost();
                }
                else if (NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
                else if (NetworkServer.active)
                {
                    NetworkManager.singleton.StopServer();
                }
            }
        }

        private void DrawLobbyBlock()
        {
            MatchManager matchManager = MatchManager.Instance;
            if (matchManager == null)
            {
                return;
            }

            GUILayout.Space(8f);
            DrawWrappedLabel("Лобби");
            DrawWrappedLabel($"Состояние: {DescribeMatchState(matchManager.State)}");
            if (matchManager.State == MatchState.Completed && matchManager.WinningPlayerSlot >= 0)
            {
                DrawWrappedLabel($"Победитель: Игрок {matchManager.WinningPlayerSlot + 1}");
            }

            if (matchManager.State == MatchState.Lobby)
            {
                if (cachedPlayers.Length < 2)
                {
                    DrawWrappedLabel("Ждем второго игрока. Новая сессия стартует автоматически.");
                }
                else
                {
                    DrawWrappedLabel("Оба игрока подключены. Сервер поднимает новую сессию.");
                }
            }
        }

        private void DrawGameplayBlock()
        {
            MatchManager matchManager = MatchManager.Instance;
            TurnManager turnManager = TurnManager.Instance;
            DiceManager diceManager = DiceManager.Instance;
            LocalPlayerController local = LocalPlayerController.Instance;
            if (matchManager == null || turnManager == null || diceManager == null || local?.Player == null)
            {
                return;
            }

            int localPlayerSlot = local.Player.PlayerSlot;
            bool canActNow = turnManager.CanPlayerAct(localPlayerSlot);
            if (canActNow)
            {
                GUILayout.Space(8f);
                DrawCharacterSelection(local, canActNow);
                DrawCardList(local, canActNow);
                DrawBoardMovementHint(local, diceManager, turnManager);

                GUILayout.Space(10f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.enabled = turnManager.CanPlayerRoll(localPlayerSlot);
                if (GUILayout.Button("Бросить кубики", wrappedButtonStyle, GUILayout.Width(GetSplitButtonWidth()), GUILayout.MinHeight(34f)))
                {
                    RequestRollDice();
                }

                GUILayout.Space(6f);

                GUI.enabled = turnManager.CanPlayerEndTurn(localPlayerSlot);
                string endTurnLabel = "Завершить ход";
                if (GUILayout.Button(endTurnLabel, wrappedButtonStyle, GUILayout.Width(GetSplitButtonWidth()), GUILayout.MinHeight(34f)))
                {
                    local.EndTurn();
                }
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

        }

        private void DrawGameplayInfoBlock()
        {
            if (!isDebugVisible)
            {
                return;
            }

            MatchManager matchManager = MatchManager.Instance;
            TurnManager turnManager = TurnManager.Instance;
            DiceManager diceManager = DiceManager.Instance;
            LocalPlayerController local = LocalPlayerController.Instance;
            if (matchManager == null || turnManager == null || diceManager == null || local?.Player == null)
            {
                return;
            }

            GUILayout.Space(8f);
            int localPlayerSlot = local.Player.PlayerSlot;
            bool canActNow = turnManager.CanPlayerAct(localPlayerSlot);
            bool canSeeRolledDice = diceManager.HasRolledThisTurn(localPlayerSlot);
            DrawWrappedLabel("Матч");
            DrawWrappedLabel($"Вы: Игрок {localPlayerSlot + 1}");
            DrawWrappedLabel($"Режим: {DescribeMode(turnManager.CurrentMode)}");
            DrawWrappedLabel($"Сейчас ходит: {DescribeTurnOwner(turnManager, localPlayerSlot)}");
            DrawWrappedLabel($"Номер хода: {turnManager.TurnNumber}");
            DrawWrappedLabel($"Фаза: {DescribePhase(turnManager.GetCurrentPhase(localPlayerSlot))}");
            DrawWrappedLabel(DescribeSetupProgress(turnManager, localPlayerSlot));
            DrawWrappedLabel(DescribeDwarfCountdown(matchManager));
            DrawWrappedLabel(GetActionHint(local, turnManager, diceManager));
            GUILayout.Space(4f);
            DrawWrappedLabel(DescribeDice(diceManager, localPlayerSlot));

            if (canSeeRolledDice)
            {
                DrawWrappedLabel($"Неиспользованных действий кубиков: {turnManager.GetRemainingDieActions(localPlayerSlot)}");

                if (turnManager.GetRemainingCardTransfers(localPlayerSlot) > 0)
                {
                    DrawWrappedLabel($"Оставшихся передач карт: {turnManager.GetRemainingCardTransfers(localPlayerSlot)}");
                }

                DrawWrappedLabel($"Бонус к перемещению: {turnManager.GetMoveBonus(localPlayerSlot)}");
                DrawWrappedLabel($"Зелёная зона: до {turnManager.GetPrimaryMoveBudget(localPlayerSlot)} шагов. Жёлтая зона: до {turnManager.GetRemainingMoveBudget(localPlayerSlot)} шагов.");
            }

            if (!canActNow)
            {
                GUILayout.Space(6f);
                DrawWrappedLabel("Ваши действия станут доступны, когда ход перейдет к вам.");
            }

            DrawConnectedPlayersBlock();
            DrawDebugBlock();
        }

        public void RequestRollDice()
        {
            LocalPlayerController local = LocalPlayerController.Instance;
            if (!CanLocalPlayerRollDice(local))
            {
                return;
            }

            local.RollDice();
            rollDiceRequestPending = true;
            rollDiceRequestPendingUntil = Time.unscaledTime + RollDicePromptPendingTimeout;
            UpdateRollDicePromptVisibility();
        }

        private void DrawCharacterSelection(LocalPlayerController local, bool isMyTurn)
        {
            List<CharacterSnapshot> ownedCharacters = local.Player.Characters
                .OrderBy(character => character.CharacterType)
                .ToList();

            GUILayout.Space(4f);
            DrawWrappedLabel("Ваши персонажи");
            DrawWrappedLabel(DescribeSelectedCharacter(local.Player));
            if (ownedCharacters.Count == 0)
            {
                DrawWrappedLabel("Персонажи еще синхронизируются...");
            }

            for (int i = 0; i < ownedCharacters.Count; i++)
            {
                CharacterSnapshot character = ownedCharacters[i];
                GUI.enabled = !character.IsDead &&
                              isMyTurn &&
                              (TurnManager.Instance == null ||
                               TurnManager.Instance.CanPlayerSelectCharacter(local.Player.PlayerSlot, character.NetId));
                bool isSelected = local.Player.SelectedCharacterNetId == character.NetId;
                string nodeLabel = BoardNodeDisplayUtility.GetDisplayName(character.CurrentNodeId);
                string healthLabel = character.IsDead ? "мертв" : $"{character.Health}/100";
                string label = isSelected
                    ? $"[{character.DisplayName}] {nodeLabel} HP {healthLabel}"
                    : $"{character.DisplayName} - {nodeLabel} HP {healthLabel}";
                if (DrawCenteredButton(label))
                {
                    local.SelectCharacter(character.NetId);
                }
            }

            GUI.enabled = true;
        }

        private void DrawCenterDiceOverlay()
        {
            MatchManager matchManager = MatchManager.Instance;
            DiceManager diceManager = DiceManager.Instance;
            LocalPlayerController local = LocalPlayerController.Instance;
            if (matchManager == null || diceManager == null || local?.Player == null)
            {
                return;
            }

            int localPlayerSlot = local.Player.PlayerSlot;
            if (!diceManager.HasRolledThisTurn(localPlayerSlot) ||
                matchManager.State == MatchState.Lobby)
            {
                return;
            }

            float overlayScale = Mathf.Clamp(CalculateGuiScale(), 0.9f, 1.1f);
            float overlayWidth = Mathf.Min(DiceOverlayWidth * overlayScale, Screen.width - (PanelPadding * 2f));
            float overlayHeight = DiceOverlayHeight * overlayScale;
            Rect overlayRect = new(
                (Screen.width - overlayWidth) * 0.5f,
                24f,
                overlayWidth,
                overlayHeight);

            GUIStyle boxStyle = new(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(20f * overlayScale),
                padding = new RectOffset(20, 20, 16, 16)
            };

            GUIStyle titleStyle = new(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(18f * overlayScale),
                fontStyle = FontStyle.Bold
            };

            GUIStyle valueStyle = new(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(28f * overlayScale),
                fontStyle = FontStyle.Bold
            };

            GUI.Box(overlayRect, GUIContent.none, boxStyle);
            GUILayout.BeginArea(overlayRect);
            GUILayout.Space(10f);
            GUILayout.Label("Публичный бросок кубиков", titleStyle);
            GUILayout.Label(DescribeDice(diceManager, localPlayerSlot), valueStyle);
            GUILayout.EndArea();
        }

        private void DrawCardList(LocalPlayerController local, bool isMyTurn)
        {
            GUILayout.Space(4f);
            DrawWrappedLabel("Ваши карты");
            GUI.enabled = CanDrawCard(local, isMyTurn);
            if (DrawCenteredButton("Взять карту из колоды"))
            {
                local.DrawCard();
            }

            GUI.enabled = true;
            uint visibleCharacterNetId = ResolveActionCharacterNetId(local);
            if (visibleCharacterNetId == 0)
            {
                DrawWrappedLabel("Выберите персонажа, чтобы увидеть его карты.");
                return;
            }

            List<CardSnapshot> visibleCards = local.Player.Cards
                .Where(card => card.AssignedCharacterNetId == visibleCharacterNetId)
                .ToList();
            if (visibleCards.Count == 0)
            {
                DrawWrappedLabel("У выбранного персонажа пока нет карт.");
                return;
            }

            for (int i = 0; i < visibleCards.Count; i++)
            {
                CardSnapshot card = visibleCards[i];
                bool canPlayCard = CanPlayCard(local, isMyTurn, card);
                string label = card.IsPlayable
                    ? $"{card.DisplayName} ({DescribeHandSlot(card.HandSlotIndex)})"
                    : $"{card.DisplayName} ({DescribeHandSlot(card.HandSlotIndex)}, ПКМ: выкинуть)";

                GUI.enabled = canPlayCard;
                bool pressed = DrawCenteredButton(label, out Rect cardButtonRect);
                GUI.enabled = true;

                Event currentEvent = Event.current;
                if (currentEvent != null &&
                    currentEvent.type == EventType.MouseDown &&
                    currentEvent.button == 1 &&
                    cardButtonRect.Contains(currentEvent.mousePosition) &&
                    card.NetId != 0)
                {
                    local.DiscardCard(card.NetId);
                    currentEvent.Use();
                    continue;
                }

                if (pressed)
                {
                    local.UseCard(card.NetId);
                }

                if (CanTransferCard(local, isMyTurn, card))
                {
                    DrawCardTransferTargets(local, card);
                }
            }

            GUI.enabled = true;
        }

        private void DrawCardTransferTargets(LocalPlayerController local, CardSnapshot card)
        {
            uint sourceCharacterNetId = ResolveActionCharacterNetId(local);
            if (sourceCharacterNetId == 0)
            {
                return;
            }

            List<CharacterSnapshot> targets = local.Player.Characters
                .Where(character => character.NetId != 0 && character.NetId != sourceCharacterNetId)
                .OrderBy(character => character.CharacterType)
                .ToList();
            for (int i = 0; i < targets.Count; i++)
            {
                CharacterSnapshot target = targets[i];
                GUI.enabled = true;
                if (DrawCenteredButton($"Передать -> {DescribeCharacterType(target.CharacterType)}"))
                {
                    local.TransferCard(card.NetId, target.NetId);
                }
            }

            GUI.enabled = true;
        }

        private void DrawBoardMovementHint(LocalPlayerController local, DiceManager diceManager, TurnManager turnManager)
        {
            int localPlayerSlot = local.Player.PlayerSlot;
            if (Board.BoardGraph.Instance == null ||
                local.Player.SelectedCharacterNetId == 0 ||
                diceManager.GetTotal(localPlayerSlot) <= 0)
            {
                return;
            }

            CharacterSnapshot selected = local.Player.Characters.FirstOrDefault(character => character.NetId == local.Player.SelectedCharacterNetId);
            if (selected.NetId == 0 || string.IsNullOrWhiteSpace(selected.CurrentNodeId))
            {
                return;
            }

            int moveBudget = turnManager.GetCurrentMoveBudget(localPlayerSlot);
            int remainingMoveBudget = turnManager.GetRemainingMoveBudget(localPlayerSlot);
            GUILayout.Space(4f);
            DrawWrappedLabel($"Перемещение: зелёный до {turnManager.GetPrimaryMoveBudget(localPlayerSlot)} шагов, жёлтый до {remainingMoveBudget} из {moveBudget}.");

            if (!turnManager.CanPlayerMove(local.Player.PlayerSlot))
            {
                DrawWrappedLabel("Бросьте кубики, выберите персонажа или используйте оставшееся действие кубика.");
                return;
            }

            List<string> destinations = Board.BoardGraph.Instance.GetReachableDestinations(selected.CurrentNodeId, remainingMoveBudget);
            DrawWrappedLabel("Наводите курсор на узлы поля: зелёный тратит первый кубик, жёлтый тратит оба, красный недоступен.");
            DrawWrappedLabel($"Доступных точек назначения сейчас: {destinations.Count}.");
        }

        private void DrawDebugBlock()
        {
            GUILayout.Space(8f);
            DrawWrappedLabel("Техническая информация");
            DrawWrappedLabel($"Match State: {MatchManager.Instance?.State}");
            DrawWrappedLabel($"Starter Setup Segments: {MatchManager.Instance?.StarterTurnsElapsed ?? 0}");
            DrawWrappedLabel($"Dwarfs Spawned: {Dwarfs.DwarfManager.Instance != null && Dwarfs.DwarfManager.Instance.DwarfsSpawned}");
            DrawWrappedLabel($"Dwarf Turns Resolved: {Dwarfs.DwarfManager.Instance?.DwarfTurnsResolved ?? 0}");
            DrawWrappedLabel($"Spawned Characters: {cachedCharacterCount}");
            DrawWrappedLabel($"Spawned Dwarfs: {cachedDwarfCount}");
        }

        private void DrawConnectedPlayersBlock()
        {
            GUILayout.Space(8f);
            DrawWrappedLabel("Подключённые игроки");

            int localSlot = LocalPlayerController.Instance?.Player != null ? LocalPlayerController.Instance.Player.PlayerSlot : -1;
            if (cachedPlayers.Length == 0)
            {
                DrawWrappedLabel("Игроки пока не подключены.");
                return;
            }

            for (int i = 0; i < cachedPlayers.Length; i++)
            {
                string ownerLabel = cachedPlayers[i].PlayerSlot == localSlot ? "Вы" : "Игрок";
                DrawWrappedLabel($"{ownerLabel} [{cachedPlayers[i].PlayerSlot + 1}]: {cachedPlayers[i].DisplayName}, подключен");
            }
        }

        private void DrawChatBlock()
        {
            if (!NetworkClient.active)
            {
                return;
            }

            GUILayout.Space(8f);
            DrawWrappedLabel("Чат");

            LocalPlayerController local = LocalPlayerController.Instance;
            if (local?.Player == null)
            {
                DrawWrappedLabel("Локальный игрок еще не создан.");
                return;
            }

            IReadOnlyList<NetworkPlayerConnection.ChatEntry> messages = NetworkPlayerConnection.ChatHistory;
            if (messages.Count != lastRenderedChatCount)
            {
                chatScrollPosition.y = float.MaxValue;
                lastRenderedChatCount = messages.Count;
            }

            chatScrollPosition = GUILayout.BeginScrollView(chatScrollPosition, GUILayout.Height(ChatLogHeight));
            if (messages.Count == 0)
            {
                GUILayout.Label("Сообщений пока нет.", wrappedLabelStyle, GUILayout.Width(GetContentWidth() - 18f));
            }
            else
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    NetworkPlayerConnection.ChatEntry message = messages[i];
                    string sender = message.SenderSlot == local.Player.PlayerSlot ? "Вы" : message.SenderName;
                    GUILayout.Label($"{sender}: {message.Message}", wrappedLabelStyle, GUILayout.Width(GetContentWidth() - 18f));
                }
            }
            GUILayout.EndScrollView();

            bool shouldSend = false;
            Event currentEvent = Event.current;

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(ChatInputControlName);
            chatInput = GUILayout.TextField(chatInput, NetworkPlayerConnection.MaxChatMessageLength, GUILayout.Width(GetContentWidth() - 90f));
            if (currentEvent.type == EventType.KeyDown &&
                (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == ChatInputControlName)
            {
                shouldSend = true;
                currentEvent.Use();
            }

            if (GUILayout.Button("Отправить", wrappedButtonStyle, GUILayout.Width(84f), GUILayout.MinHeight(24f)))
            {
                shouldSend = true;
            }
            GUILayout.EndHorizontal();

            if (!shouldSend)
            {
                return;
            }

            string messageToSend = chatInput.Trim();
            if (string.IsNullOrEmpty(messageToSend))
            {
                return;
            }

            local.SendChatMessage(messageToSend);
            chatInput = string.Empty;
            GUI.FocusControl(ChatInputControlName);
        }

        private void UpdateGuiStyles()
        {
            if (wrappedLabelStyle == null)
            {
                wrappedLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    stretchWidth = false
                };
            }

            if (wrappedButtonStyle == null)
            {
                wrappedButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    wordWrap = true,
                    stretchWidth = false,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            verticalScrollbarStyle ??= new GUIStyle(GUI.skin.verticalScrollbar);
        }

        private float GetContentWidth()
        {
            float scrollbarWidth = verticalScrollbarStyle.fixedWidth > 0f ? verticalScrollbarStyle.fixedWidth : 16f;
            return Mathf.Max(140f, currentPanelWidth - PanelInnerPadding - scrollbarWidth - ContentSafetyPadding);
        }

        private float GetButtonWidth()
        {
            return Mathf.Max(120f, GetContentWidth() * ButtonWidthFactor);
        }

        private float GetSplitButtonWidth()
        {
            return Mathf.Max(56f, (GetButtonWidth() * 0.5f) - 3f);
        }

        private void DrawWrappedLabel(string text)
        {
            GUILayout.Label(text, wrappedLabelStyle, GUILayout.Width(GetContentWidth()));
        }

        private bool DrawCenteredButton(string text)
        {
            return DrawCenteredButton(text, out _);
        }

        private bool DrawCenteredButton(string text, out Rect buttonRect)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool pressed = GUILayout.Button(text, wrappedButtonStyle, GUILayout.Width(GetButtonWidth()), GUILayout.MinHeight(34f));
            buttonRect = GUILayoutUtility.GetLastRect();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return pressed;
        }

        private void ApplyConnectionSettings()
        {
            string selectedAddress = ResolveSelectedAddress();
            ushort selectedPort = ResolveSelectedPort();

            PlayerPrefs.SetString(PortPrefKey, selectedPort.ToString());
            PlayerPrefs.Save();

            if (NetworkManager.singleton is RramNetworkManager runtimeManager)
            {
                runtimeManager.ApplyConnectionSettings(selectedAddress, selectedPort);
                return;
            }

            NetworkManager.singleton.networkAddress = selectedAddress;
        }

        private string ResolveSelectedAddress()
        {
            if (connectionTarget == ConnectionTarget.Localhost)
            {
                return "localhost";
            }

            return FixedRemoteAddress;
        }

        private ushort ResolveSelectedPort()
        {
            if (ushort.TryParse(port, out ushort parsedPort) && parsedPort > 0)
            {
                return parsedPort;
            }

            return MatchContext.Instance?.Config?.NetworkPort ?? 7777;
        }

        private static string DescribePlayer(int playerSlot, int localPlayerSlot)
        {
            if (playerSlot < 0)
            {
                return "никто";
            }

            return playerSlot == localPlayerSlot ? $"Вы (Игрок {playerSlot + 1})" : $"Соперник (Игрок {playerSlot + 1})";
        }

        private static string DescribeTurnOwner(TurnManager turnManager, int localPlayerSlot)
        {
            if (turnManager == null)
            {
                return "неизвестно";
            }

            if (turnManager.IsSetupPhase)
            {
                return "оба игрока";
            }

            return DescribePlayer(turnManager.CurrentPlayerSlot, localPlayerSlot);
        }

        private static string DescribeMode(TurnMode mode)
        {
            return mode switch
            {
                TurnMode.Setup => "стартовая подготовка",
                TurnMode.Alternating => "по очереди",
                _ => mode.ToString()
            };
        }

        private static string DescribePhase(TurnPhase phase)
        {
            return phase switch
            {
                TurnPhase.WaitingForRoll => "ожидание броска",
                TurnPhase.WaitingForMove => "ожидание перемещения",
                TurnPhase.WaitingForEndTurn => "можно завершать ход",
                _ => phase.ToString()
            };
        }

        private static string DescribeMatchState(MatchState state)
        {
            return state switch
            {
                MatchState.Bootstrapping => "инициализация",
                MatchState.Lobby => "лобби",
                MatchState.Starting => "старт матча",
                MatchState.PlayerTurn => "ход игрока",
                MatchState.ResolvingDwarfs => "ход дварфов",
                MatchState.Completed => "матч завершен",
                _ => state.ToString()
            };
        }

        private static string DescribeDwarfCountdown(MatchManager matchManager)
        {
            if (matchManager == null || MatchContext.Instance?.TurnManager == null)
            {
                return "Дварфы: конфиг недоступен";
            }

            if (Dwarfs.DwarfManager.Instance != null && Dwarfs.DwarfManager.Instance.DwarfsSpawned)
            {
                return "Дварфы уже на поле";
            }

            TurnManager turnManager = MatchContext.Instance.TurnManager;
            if (!turnManager.IsSetupPhase)
            {
                return "Дварфы уже вышли и участвуют в матче";
            }

            int remainingTurns = turnManager.GetTotalRemainingSetupTurns();
            return $"До выхода дварфов осталось стартовых ходов: {remainingTurns}";
        }

        private static string DescribeDice(DiceManager diceManager, int localPlayerSlot)
        {
            if (!diceManager.HasRolledThisTurn(localPlayerSlot))
            {
                return "Кубики еще не брошены.";
            }

            return $"Вы: {diceManager.GetDieA(localPlayerSlot)} + {diceManager.GetDieB(localPlayerSlot)} = {diceManager.GetTotal(localPlayerSlot)}";
        }

        private static string DescribeSelectedCharacter(NetworkPlayerConnection player)
        {
            if (player == null || player.SelectedCharacterNetId == 0)
            {
                return "Выбранный персонаж: не выбран";
            }

            for (int i = 0; i < player.Characters.Count; i++)
            {
                CharacterSnapshot selected = player.Characters[i];
                if (selected.NetId == player.SelectedCharacterNetId)
                {
                    string health = selected.IsDead ? "мертв" : $"{selected.Health}/100";
                    return $"Выбран: {selected.DisplayName} ({BoardNodeDisplayUtility.GetDisplayName(selected.CurrentNodeId)}, HP {health})";
                }
            }

            return "Выбранный персонаж: не найден";
        }

        private static string DescribeCharacterType(CharacterType type)
        {
            return type switch
            {
                CharacterType.Blacksmith => "Кузнец",
                CharacterType.BlacksmithAssistant => "Помощник",
                CharacterType.Warrior => "Воин",
                CharacterType.Hunter => "Охотник",
                CharacterType.Shaman => "Шаман",
                _ => type.ToString()
            };
        }

        private static string GetActionHint(LocalPlayerController local, TurnManager turnManager, DiceManager diceManager)
        {
            int localPlayerSlot = local.Player.PlayerSlot;
            if (!turnManager.CanPlayerAct(localPlayerSlot))
            {
                if (turnManager.IsSetupPhase)
                {
                    return turnManager.GetRemainingSetupTurns(localPlayerSlot) <= 0
                        ? "Первые стартовые ходы завершены."
                        : "Сейчас стартовый ход соперника. Значения его кубиков скрыты.";
                }

                return "Сейчас ход соперника. Ждите его действий.";
            }

            TurnPhase phase = turnManager.GetCurrentPhase(localPlayerSlot);
            return phase switch
            {
                TurnPhase.WaitingForRoll => turnManager.IsSetupPhase
                    ? "Стартовый ход: бросьте кубики, затем выберите персонажа."
                    : "Ваш ход: бросьте кубики, затем выберите персонажа.",
                TurnPhase.WaitingForMove when local.Player.SelectedCharacterNetId == 0 => "Сначала выберите персонажа.",
                TurnPhase.WaitingForMove when !turnManager.IsSetupPhase && !diceManager.HasRolledThisTurn(localPlayerSlot) => "Сначала бросьте кубики.",
                TurnPhase.WaitingForMove when turnManager.IsSetupPhase => "Стартовый ход: походите выбранным персонажем по результату броска.",
                TurnPhase.WaitingForMove when turnManager.HasPlayerMovedThisTurn(localPlayerSlot) && turnManager.GetRemainingMoveBudget(localPlayerSlot) > 0 => "Можно продолжать движение только тем персонажем, который уже начал ходить, либо завершить ход.",
                TurnPhase.WaitingForMove => "Можно действовать выбранным персонажем: перемещаться, брать карту, использовать карту или передавать карты.",
                TurnPhase.WaitingForEndTurn => turnManager.IsSetupPhase
                    ? "Стартовое действие завершено. Завершите ход."
                    : "Ваш ход: перемещение завершено, можно завершать ход.",
                _ => "Следуйте текущей фазе хода."
            };
        }

        private static string DescribeSetupProgress(TurnManager turnManager, int localPlayerSlot)
        {
            if (turnManager == null || !turnManager.IsSetupPhase)
            {
                return "Стартовая подготовка завершена.";
            }

            return $"Стартовые ходы до выхода дварфов: {turnManager.GetRemainingSetupTurns(localPlayerSlot)}.";
        }

        private static string GetTurnWord(int value)
        {
            int absValue = Mathf.Abs(value) % 100;
            int lastDigit = absValue % 10;
            if (absValue is >= 11 and <= 19)
            {
                return "ходов";
            }

            return lastDigit switch
            {
                1 => "ход",
                2 or 3 or 4 => "хода",
                _ => "ходов"
            };
        }

        private static bool CanPlayCard(LocalPlayerController local, bool isMyTurn, CardSnapshot card)
        {
            if (!isMyTurn || local?.Player == null || TurnManager.Instance == null)
            {
                return false;
            }

            if (TryResolveLocalCardInstance(card.NetId, out CardInstance cardInstance))
            {
                return cardInstance.CanUseFromLocalClient();
            }

            return card.NetId != 0 &&
                   card.IsPlayable &&
                   !card.RequiresTransferBeforeUse &&
                   TurnManager.Instance.CanPlayerSelectCharacter(local.Player.PlayerSlot, card.AssignedCharacterNetId) &&
                   TurnManager.Instance.CanPlayerSpendDieActionWithMinimum(local.Player.PlayerSlot, card.MinimumDieValue) &&
                   HasLocalCardRequirements(local.Player, card);
        }

        private static bool TryResolveLocalCardInstance(uint cardNetId, out CardInstance cardInstance)
        {
            cardInstance = null;
            return cardNetId != 0 &&
                   NetworkClient.spawned.TryGetValue(cardNetId, out NetworkIdentity identity) &&
                   identity != null &&
                   identity.TryGetComponent(out cardInstance);
        }

        private static bool HasLocalCardRequirements(NetworkPlayerConnection player, CardSnapshot card)
        {
            if (player == null)
            {
                return false;
            }

            return card.CardId switch
            {
                "BagCard" => CountLocalCards(player, "DirtyMixedIronOreCard") >= 3 ||
                             CountLocalCards(player, "GoldNuggetCard") >= 1,
                "BagRecipeCard" => CountLocalCards(player, "HammerCard") >= 1 &&
                                   CountLocalCards(player, "CleanedRamHideCard") >= 1 &&
                                   CountLocalCards(player, "RamWoolThreadBallCard") >= 1 &&
                                   CountLocalCards(player, "ShamanCarpetCard") >= 1,
                "BowRecipeCard" => CountLocalCards(player, "HammerCard") >= 1 &&
                                   CountLocalCards(player, "FlexibleStickCard") >= 1 &&
                                   CountLocalCards(player, "RamWoolThreadBallCard") >= 1,
                "ClubBlueprintCard" => CountLocalCards(player, "HammerCard") >= 1 &&
                                       (CountLocalCards(player, "BearHideCard") >= 1 ||
                                        CountLocalCards(player, "RamHideCard") >= 1),
                "HammerBlueprintCard" => CountLocalCards(player, "MixedIronOreCard") >= 1,
                "ShamanCarpetRecipeCard" => CountLocalCards(player, "BearHideCard") >= 1 &&
                                            CountLocalCards(player, "RamHideThreadCard") >= 1,
                _ => true
            };
        }

        private static int CountLocalCards(NetworkPlayerConnection player, string cardId)
        {
            if (player == null || string.IsNullOrWhiteSpace(cardId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < player.Cards.Count; i++)
            {
                CardSnapshot candidate = player.Cards[i];
                if (candidate.CardId == cardId)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool CanDrawCard(LocalPlayerController local, bool isMyTurn)
        {
            TurnManager turnManager = TurnManager.Instance;
            if (!isMyTurn ||
                local?.Player == null ||
                turnManager == null ||
                BoardGraph.Instance == null ||
                !turnManager.CanPlayerSpendDieAction(local.Player.PlayerSlot))
            {
                return false;
            }

            uint activeCharacterNetId = turnManager.GetActiveCharacterNetId(local.Player.PlayerSlot);
            uint characterNetId = activeCharacterNetId != 0
                ? activeCharacterNetId
                : local.Player.SelectedCharacterNetId;
            if (characterNetId == 0)
            {
                return false;
            }

            CharacterSnapshot selected = local.Player.Characters.FirstOrDefault(character => character.NetId == characterNetId);
            if (selected.NetId == 0 ||
                string.IsNullOrWhiteSpace(selected.CurrentNodeId) ||
                !BoardGraph.Instance.TryGetNode(selected.CurrentNodeId, out BoardNode node))
            {
                return false;
            }

            bool isDeckNode = node.NodeKind == BoardNodeKind.GreenDeck || node.NodeKind == BoardNodeKind.RedDeck;
            return isDeckNode && !turnManager.HasDrawnFromDeckNode(local.Player.PlayerSlot, node.NodeId);
        }

        private static bool CanTransferCard(LocalPlayerController local, bool isMyTurn, CardSnapshot card)
        {
            TurnManager turnManager = TurnManager.Instance;
            if (!isMyTurn ||
                local?.Player == null ||
                turnManager == null ||
                card.NetId == 0 ||
                !turnManager.CanPlayerTransferCard(local.Player.PlayerSlot))
            {
                return false;
            }

            uint sourceCharacterNetId = ResolveActionCharacterNetId(local);
            return sourceCharacterNetId != 0 && card.AssignedCharacterNetId == sourceCharacterNetId;
        }

        private static uint ResolveActionCharacterNetId(LocalPlayerController local)
        {
            if (local?.Player == null || TurnManager.Instance == null)
            {
                return 0;
            }

            uint activeCharacterNetId = TurnManager.Instance.GetActiveCharacterNetId(local.Player.PlayerSlot);
            return activeCharacterNetId != 0
                ? activeCharacterNetId
                : local.Player.SelectedCharacterNetId;
        }

        private static string DescribeHandSlot(int handSlotIndex)
        {
            if (handSlotIndex >= 0 &&
                handSlotIndex <= byte.MaxValue &&
                System.Enum.IsDefined(typeof(CharacterType), (byte)handSlotIndex))
            {
                return DescribeCharacterType((CharacterType)(byte)handSlotIndex);
            }

            return $"Слот {handSlotIndex + 1}";
        }

        private void UpdateRollDicePromptVisibility()
        {
            if (rollDicePromptObject == null)
            {
                return;
            }

            bool canShowPrompt = CanLocalPlayerRollDice(LocalPlayerController.Instance);
            if (!canShowPrompt)
            {
                rollDiceRequestPending = false;
                rollDiceRequestPendingUntil = 0f;
            }
            else if (rollDiceRequestPending && Time.unscaledTime >= rollDiceRequestPendingUntil)
            {
                rollDiceRequestPending = false;
                rollDiceRequestPendingUntil = 0f;
            }

            bool shouldShowPrompt = canShowPrompt && !rollDiceRequestPending;
            if (rollDicePromptObject.activeSelf != shouldShowPrompt)
            {
                rollDicePromptObject.SetActive(shouldShowPrompt);
            }
        }

        private static bool CanLocalPlayerRollDice(LocalPlayerController local)
        {
            return local?.Player != null &&
                   TurnManager.Instance != null &&
                   TurnManager.Instance.CanPlayerRoll(local.Player.PlayerSlot);
        }
    }
}
