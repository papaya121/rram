using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRaM.Core.Cards
{
    public sealed class CardView : MonoBehaviour
    {
        public Image image;
        public TMP_Text title;

        [SerializeField] private Canvas canvasRoot;
        [SerializeField] private Sprite hiddenImage;
        [SerializeField] private string hiddenTitle = "Hidden Card";
        [SerializeField] private bool overrideSorting = true;
        [SerializeField] private int sortingOrder = 10;
        [SerializeField] private Vector3 canvasLocalPositionOffset = Vector3.zero;

        private Vector3 initialCanvasLocalPosition;
        private bool hasInitialCanvasPose;

        private void Awake()
        {
            AutoBind();
            ApplyCanvasSettings();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoBind();
            ApplyCanvasSettings();
        }
#endif

        public void Bind(BaseCard data, bool revealFace = true)
        {
            AutoBind();
            ApplyCanvasSettings();

            if (image != null)
            {
                image.sprite = revealFace ? data?.cardImage : hiddenImage;
                image.enabled = revealFace ? data?.cardImage != null : hiddenImage != null;
            }

            if (title != null)
            {
                title.text = revealFace
                    ? (data != null && !string.IsNullOrWhiteSpace(data.cardName) ? data.cardName : string.Empty)
                    : hiddenTitle;
            }
        }

        private void AutoBind()
        {
            image ??= GetComponentInChildren<Image>(true);
            title ??= GetComponentInChildren<TMP_Text>(true);
            canvasRoot ??= GetComponentInChildren<Canvas>(true);

            if (canvasRoot != null && !hasInitialCanvasPose)
            {
                initialCanvasLocalPosition = canvasRoot.transform.localPosition;
                hasInitialCanvasPose = true;
            }
        }

        private void ApplyCanvasSettings()
        {
            if (canvasRoot == null)
            {
                return;
            }

            canvasRoot.enabled = true;
            canvasRoot.overrideSorting = overrideSorting;
            canvasRoot.sortingOrder = sortingOrder;

            if (hasInitialCanvasPose)
            {
                canvasRoot.transform.localPosition = initialCanvasLocalPosition + canvasLocalPositionOffset;
            }
        }
    }
}
