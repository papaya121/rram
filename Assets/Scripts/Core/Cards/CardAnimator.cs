using System.Collections;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public sealed class CardAnimator : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float drawDuration = 0.4f;
        [SerializeField] private float hoverDuration = 0.12f;
        [SerializeField] private float useDuration = 0.28f;
        [SerializeField] private AnimationCurve motionCurve = null;
        [SerializeField] private Vector3 hoverOffset = new(0f, 0.2f, 0f);
        [SerializeField] private Vector3 useOffset = new(0f, 0.35f, 0f);
        [SerializeField] private Vector3 useScaleMultiplier = new(1.15f, 1.15f, 1.15f);

        private Coroutine activeRootRoutine;
        private Coroutine activeVisualRoutine;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation = Quaternion.identity;
        private Vector3 restLocalScale = Vector3.one;
        private Vector3 visualRestLocalPosition;
        private Quaternion visualRestLocalRotation = Quaternion.identity;
        private Vector3 visualRestLocalScale = Vector3.one;
        private bool isHovered;

        public Transform VisualRoot => visualRoot;

        private void Awake()
        {
            motionCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            visualRoot ??= transform.Find("Model");
            visualRoot ??= transform.childCount > 0 ? transform.GetChild(0) : transform;

            restLocalPosition = transform.localPosition;
            restLocalRotation = transform.localRotation;
            restLocalScale = transform.localScale;
            visualRestLocalPosition = visualRoot.localPosition;
            visualRestLocalRotation = visualRoot.localRotation;
            visualRestLocalScale = visualRoot.localScale;
        }

        public void SetLayout(Vector3 targetLocalPosition, Quaternion targetLocalRotation, Vector3 targetLocalScale, bool animate)
        {
            restLocalPosition = targetLocalPosition;
            restLocalRotation = targetLocalRotation;
            restLocalScale = targetLocalScale;

            if (!animate)
            {
                ApplyRestPose();
                return;
            }

            RestartRootRoutine(AnimateRootPose(restLocalPosition, restLocalRotation, restLocalScale, drawDuration));
        }

        public void SetHovered(bool hovered)
        {
            isHovered = hovered;
            RestartVisualRoutine(AnimateVisualPose(GetDisplayedVisualLocalPosition(), visualRestLocalRotation, visualRestLocalScale, hoverDuration));
        }

        public void ClearHoverImmediate()
        {
            isHovered = false;

            if (activeVisualRoutine != null)
            {
                StopCoroutine(activeVisualRoutine);
                activeVisualRoutine = null;
            }

            ApplyVisualRestPose();
        }

        public void CancelRootAnimation()
        {
            if (activeRootRoutine == null)
            {
                return;
            }

            StopCoroutine(activeRootRoutine);
            activeRootRoutine = null;
        }

        public IEnumerator PlayLocalPoseAnimation(Vector3 targetLocalPosition, Quaternion targetLocalRotation, Vector3 targetLocalScale, float duration)
        {
            CancelRootAnimation();
            yield return AnimateRootPose(targetLocalPosition, targetLocalRotation, targetLocalScale, duration);
        }

        public IEnumerator PlayDrawAnimation(Transform target)
        {
            if (target == null)
            {
                yield break;
            }

            yield return AnimateWorldRootPose(target.position, target.rotation, drawDuration);
        }

        public IEnumerator PlayUseAnimation()
        {
            Vector3 startLocalPosition = visualRoot.localPosition;
            Quaternion startLocalRotation = visualRoot.localRotation;
            Vector3 startLocalScale = visualRoot.localScale;

            Vector3 targetLocalPosition = startLocalPosition + useOffset;
            Quaternion targetLocalRotation = startLocalRotation * Quaternion.Euler(-18f, 180f, 0f);
            Vector3 targetLocalScale = Vector3.Scale(startLocalScale, useScaleMultiplier);

            float duration = Mathf.Max(0.01f, useDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = motionCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
                visualRoot.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, t);
                visualRoot.localRotation = Quaternion.Slerp(startLocalRotation, targetLocalRotation, t);
                visualRoot.localScale = Vector3.Lerp(startLocalScale, targetLocalScale, t);
                yield return null;
            }

            visualRoot.localPosition = targetLocalPosition;
            visualRoot.localRotation = targetLocalRotation;
            visualRoot.localScale = targetLocalScale;
        }

        private IEnumerator AnimateRootPose(Vector3 targetLocalPosition, Quaternion targetLocalRotation, Vector3 targetLocalScale, float duration)
        {
            Vector3 startLocalPosition = transform.localPosition;
            Quaternion startLocalRotation = transform.localRotation;
            Vector3 startLocalScale = transform.localScale;
            float elapsed = 0f;
            float clampedDuration = Mathf.Max(0.01f, duration);

            while (elapsed < clampedDuration)
            {
                elapsed += Time.deltaTime;
                float t = motionCurve.Evaluate(Mathf.Clamp01(elapsed / clampedDuration));
                transform.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, t);
                transform.localRotation = Quaternion.Slerp(startLocalRotation, targetLocalRotation, t);
                transform.localScale = Vector3.Lerp(startLocalScale, targetLocalScale, t);
                yield return null;
            }

            transform.localPosition = targetLocalPosition;
            transform.localRotation = targetLocalRotation;
            transform.localScale = targetLocalScale;
            ApplyVisualRestPose();
        }

        private IEnumerator AnimateVisualPose(Vector3 targetLocalPosition, Quaternion targetLocalRotation, Vector3 targetLocalScale, float duration)
        {
            Vector3 startLocalPosition = visualRoot.localPosition;
            Quaternion startLocalRotation = visualRoot.localRotation;
            Vector3 startLocalScale = visualRoot.localScale;
            float elapsed = 0f;
            float clampedDuration = Mathf.Max(0.01f, duration);

            while (elapsed < clampedDuration)
            {
                elapsed += Time.deltaTime;
                float t = motionCurve.Evaluate(Mathf.Clamp01(elapsed / clampedDuration));
                visualRoot.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, t);
                visualRoot.localRotation = Quaternion.Slerp(startLocalRotation, targetLocalRotation, t);
                visualRoot.localScale = Vector3.Lerp(startLocalScale, targetLocalScale, t);
                yield return null;
            }

            visualRoot.localPosition = targetLocalPosition;
            visualRoot.localRotation = targetLocalRotation;
            visualRoot.localScale = targetLocalScale;
        }

        private IEnumerator AnimateWorldRootPose(Vector3 targetPosition, Quaternion targetRotation, float duration)
        {
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float elapsed = 0f;
            float clampedDuration = Mathf.Max(0.01f, duration);

            while (elapsed < clampedDuration)
            {
                elapsed += Time.deltaTime;
                float t = motionCurve.Evaluate(Mathf.Clamp01(elapsed / clampedDuration));
                transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                yield return null;
            }

            transform.position = targetPosition;
            transform.rotation = targetRotation;
            ApplyVisualRestPose();
        }

        private Vector3 GetDisplayedVisualLocalPosition()
        {
            return visualRestLocalPosition + (isHovered ? hoverOffset : Vector3.zero);
        }

        private void ApplyRestPose()
        {
            transform.localPosition = restLocalPosition;
            transform.localRotation = restLocalRotation;
            transform.localScale = restLocalScale;
            ApplyVisualRestPose();
        }

        private void ApplyVisualRestPose()
        {
            visualRoot.localPosition = GetDisplayedVisualLocalPosition();
            visualRoot.localRotation = visualRestLocalRotation;
            visualRoot.localScale = visualRestLocalScale;
        }

        private void RestartRootRoutine(IEnumerator routine)
        {
            if (activeRootRoutine != null)
            {
                StopCoroutine(activeRootRoutine);
            }

            activeRootRoutine = StartCoroutine(routine);
        }

        private void RestartVisualRoutine(IEnumerator routine)
        {
            if (activeVisualRoutine != null)
            {
                StopCoroutine(activeVisualRoutine);
            }

            activeVisualRoutine = StartCoroutine(routine);
        }
    }
}
