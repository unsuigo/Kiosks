using Kiosk.QR;
using Mediapipe.Unity.Sample.FaceDetection;
using UnityEngine;

namespace Kiosk.Camera
{
    public sealed class KioskCameraBridge : MonoBehaviour
    {
        [SerializeField] private KioskFaceDetectorRunner faceDetector;
        [SerializeField] private TextureQrCodeScanner qrScanner;
        [SerializeField] private bool startScannerWhenReady = true;

        private Texture assignedTexture;
        private bool scannerStarted;

        private void Update()
        {
            if (faceDetector == null || qrScanner == null)
                return;

            Texture texture = faceDetector.CurrentCameraTexture;

            if (texture == null ||
                texture.width <= 16 ||
                texture.height <= 16)
            {
                return;
            }

            if (assignedTexture != texture)
            {
                assignedTexture = texture;

                qrScanner.SetSourceTexture(texture);

                Debug.Log(
                    $"[Kiosk Camera] Shared texture assigned: " +
                    $"{texture.width}x{texture.height}",
                    this);
            }

            if (startScannerWhenReady && !scannerStarted)
            {
                qrScanner.StartScanning();
                scannerStarted = true;

                Debug.Log(
                    "[Kiosk Camera] QR scanner started.",
                    this);
            }
        }

        private void OnDisable()
        {
            scannerStarted = false;

            if (qrScanner != null && qrScanner.IsScanning)
                qrScanner.StopScanning();
        }
    }
}