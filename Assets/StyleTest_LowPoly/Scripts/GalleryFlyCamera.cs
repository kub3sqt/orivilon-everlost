using UnityEngine;

namespace Orivilon.StyleTest
{
    /// <summary>
    /// Volná kamera jen pro testovací scénu AssetGallery (kolo 13). Staré Input API jako zbytek projektu.
    /// Pravé tlačítko myši = rozhlížení, WASD/QE = pohyb, Shift = rychleji, 1–9 = připravené pohledy.
    /// Do herních scén se nepoužívá.
    /// </summary>
    public class GalleryFlyCamera : MonoBehaviour
    {
        [SerializeField] private float speed = 8f;
        [SerializeField] private float lookSpeed = 3f;
        [SerializeField] private Vector3[] viewPositions;
        [SerializeField] private Vector3[] viewTargets;

        private float yaw, pitch;

        public int ViewCount => viewPositions != null ? viewPositions.Length : 0;

        private void Start()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void SetViews(Vector3[] positions, Vector3[] targets)
        {
            viewPositions = positions;
            viewTargets = targets;
        }

        public void SetView(int i)
        {
            if (viewPositions == null || viewTargets == null || i < 0 || i >= viewPositions.Length) return;
            SetPose(viewPositions[i], viewTargets[i]);
        }

        public void SetPose(Vector3 position, Vector3 target)
        {
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(target - position);
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private void Update()
        {
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetView(i);

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSpeed;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Vector3 move = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            float s = speed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            transform.position += transform.TransformDirection(move) * (s * Time.unscaledDeltaTime);
        }
    }
}
