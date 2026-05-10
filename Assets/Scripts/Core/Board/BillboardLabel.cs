using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Rotates a world-space label towards the active camera.
    /// </summary>
    public sealed class BillboardLabel : MonoBehaviour
    {
        private Camera cachedCamera;

        private void LateUpdate()
        {
            Camera targetCamera = ResolveCamera();
            if (targetCamera == null)
            {
                return;
            }

            Vector3 forward = transform.position - targetCamera.transform.position;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 euler = rotation.eulerAngles;
            euler.y = 0f;
            transform.rotation = Quaternion.Euler(euler);
        }

        private Camera ResolveCamera()
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                cachedCamera = Camera.main;
            }

            return cachedCamera;
        }
    }
}
