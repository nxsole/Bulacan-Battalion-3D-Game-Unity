using System.Collections;
using UnityEngine;

namespace TiradPass
{
    public class GoyoLastStandShot : MonoBehaviour
    {
        public Camera shotCamera;
        public Transform goyo;
        public Transform trackingStart;
        public Transform trackingEnd;
        public Transform[] barricadePieces;
        public ParticleSystem battleSmoke;
        public CanvasGroup fade;
        public float duration = 14f;
        public float collapseStartsAt = 4f;
        public float collapseInterval = 0.45f;
        public float collapseDuration = 1.3f;
        public float finalFogDensity = 0.025f;
        private Animator actorAnimator;
        private Transform pointingArm;
        private float originalFogDensity;
        private bool running;

        private void Start()
        {
            if (shotCamera == null || goyo == null || trackingStart == null || trackingEnd == null)
            {
                Debug.LogError("Goyo shot needs a camera, Goyo, and both tracking markers.", this);
                return;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            actorAnimator = goyo.GetComponentInChildren<Animator>();
            if (actorAnimator != null && actorAnimator.isHuman)
                pointingArm = actorAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            originalFogDensity = RenderSettings.fog ? RenderSettings.fogDensity : 0.001f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.59f, 0.62f, 0.64f);
            if (fade != null) { fade.alpha = 1f; fade.blocksRaycasts = false; }
            if (battleSmoke != null) battleSmoke.Play();
            StartCoroutine(PlayShot());
        }

        private IEnumerator PlayShot()
        {
            running = true;
            int collapsed = 0;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                // Linear tracking maintains a constant dolly speed through the shot.
                shotCamera.transform.position = Vector3.Lerp(trackingStart.position, trackingEnd.position, progress);
                shotCamera.transform.LookAt(goyo.position + Vector3.up * 1.25f);
                if (fade != null) fade.alpha = 1f - Mathf.Clamp01(elapsed / 1.2f);
                RenderSettings.fogDensity = Mathf.Lerp(originalFogDensity, finalFogDensity, progress);
                if (barricadePieces != null && collapsed < barricadePieces.Length
                    && elapsed >= collapseStartsAt + collapsed * collapseInterval)
                    StartCoroutine(Collapse(barricadePieces[collapsed++]));
                yield return null;
            }
            shotCamera.transform.position = trackingEnd.position;
            shotCamera.transform.LookAt(goyo.position + Vector3.up * 1.25f);
            if (fade != null) fade.alpha = 0f;
            // Hold this composition; later ending shots can continue from here.
        }

        private void LateUpdate()
        {
            if (!running || pointingArm == null) return;
            // Aim the arm across the pass without requiring a new animation clip.
            Vector3 desired = (goyo.forward + Vector3.up * 0.12f).normalized;
            var lower = actorAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (lower != null)
                pointingArm.rotation = Quaternion.FromToRotation(lower.position - pointingArm.position, desired) * pointingArm.rotation;
        }

        private IEnumerator Collapse(Transform piece)
        {
            if (piece == null) yield break;
            Vector3 start = piece.position;
            Quaternion rotation = piece.rotation;
            Vector3 end = start + goyo.right * 0.6f - Vector3.up * 0.5f;
            Quaternion endRotation = rotation * Quaternion.Euler(28f, 15f, 38f);
            for (float t = 0f; t < collapseDuration; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.Clamp01(t / Mathf.Max(0.01f, collapseDuration));
                piece.position = Vector3.Lerp(start, end, p * p);
                piece.rotation = Quaternion.Slerp(rotation, endRotation, p * p);
                yield return null;
            }
            piece.SetPositionAndRotation(end, endRotation);
        }
    }
}
