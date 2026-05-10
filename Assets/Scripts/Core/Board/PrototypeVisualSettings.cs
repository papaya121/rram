using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Stores inspector-assigned materials used by prototype runtime visuals.
    /// </summary>
    public sealed class PrototypeVisualSettings : MonoBehaviour
    {
        public static PrototypeVisualSettings Instance { get; private set; }

        [SerializeField] private Material baseMaterial;

        /// <summary>
        /// Gets the template material assigned in the inspector.
        /// </summary>
        public Material BaseMaterial => baseMaterial;

        public void Configure(Material material)
        {
            baseMaterial = material;
            PrototypeVisualFactory.Configure(baseMaterial);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            PrototypeVisualFactory.Configure(baseMaterial);
        }

        private void OnValidate()
        {
            if (Instance == this)
            {
                PrototypeVisualFactory.Configure(baseMaterial);
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
