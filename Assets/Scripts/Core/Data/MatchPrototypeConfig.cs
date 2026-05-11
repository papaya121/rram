using System;
using System.Collections.Generic;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Data
{
    /// <summary>
    /// Holds editable prototype settings for the first online gameplay slice.
    /// </summary>
    [CreateAssetMenu(menuName = "RRaM/Prototype/Match Config", fileName = "MatchPrototypeConfig")]
    public sealed class MatchPrototypeConfig : ScriptableObject
    {
        [Serializable]
        public struct StarterCardDefinition
        {
            [SerializeField] private BaseCard card;
            [SerializeField] private CharacterType assignedCharacter;

            public BaseCard Card => card;
            public CharacterType AssignedCharacter => assignedCharacter;
        }

        private const ushort DefaultNetworkPort = 7777;
        private const string DefaultLoopbackAddress = "localhost";

        [SerializeField] private int setupTurnsPerPlayer = 10;
        [SerializeField] private int dwarfTurnsAfterSetup = 10;
        [SerializeField] private int dwarfStepPerTurn = 1;
        [SerializeField] private List<StarterCardDefinition> starterCards = new();
        [SerializeField] private ushort networkPort = DefaultNetworkPort;
        [SerializeField] private string defaultAddress = DefaultLoopbackAddress;

        public int SetupTurnsPerPlayer => Mathf.Max(1, setupTurnsPerPlayer);
        public int DwarfTurnsAfterSetup => Mathf.Max(1, dwarfTurnsAfterSetup);
        public int DwarfStepPerTurn => Mathf.Max(1, dwarfStepPerTurn);
        public IReadOnlyList<StarterCardDefinition> StarterCards
        {
            get
            {
                starterCards ??= new List<StarterCardDefinition>();
                return starterCards;
            }
        }

        public ushort NetworkPort => networkPort == 0 ? DefaultNetworkPort : networkPort;
        public string DefaultAddress => string.IsNullOrWhiteSpace(defaultAddress) ? DefaultLoopbackAddress : defaultAddress;

        public void SetNetworkPort(ushort port)
        {
            networkPort = port == 0 ? DefaultNetworkPort : port;
        }

        public void SetDefaultAddress(string address)
        {
            defaultAddress = string.IsNullOrWhiteSpace(address) ? DefaultLoopbackAddress : address.Trim();
        }

        public void ApplyCommandLineOverridesFromEnvironment()
        {
            ApplyCommandLineOverrides(this);
        }

        private static void ApplyCommandLineOverrides(MatchPrototypeConfig config)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (TryGetArgumentValue(args, "-port", out string portValue) ||
                TryGetArgumentValue(args, "-networkPort", out portValue))
            {
                if (ushort.TryParse(portValue, out ushort port) && port > 0)
                {
                    config.SetNetworkPort(port);
                }
            }

            if (TryGetArgumentValue(args, "-address", out string addressValue) ||
                TryGetArgumentValue(args, "-connectAddress", out addressValue))
            {
                config.SetDefaultAddress(addressValue);
            }
        }

        private static bool TryGetArgumentValue(string[] args, string key, out string value)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = args[i + 1];
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
