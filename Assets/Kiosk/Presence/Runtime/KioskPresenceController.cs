using System;
using Kiosk.ComputerVision.FaceDetection;
using Kiosk.QR;
using UnityEngine;

namespace Kiosk.Presence
{
    public sealed class KioskPresenceController : MonoBehaviour
    {
        [Header("Sensors")]
        [SerializeField] private MonoBehaviour faceDetectorSource;
        [SerializeField] private MonoBehaviour qrScannerSource;

        [Header("Presence")]
        [Min(1f)]
        [SerializeField] private float inactivityTimeout = 10f;

        private IFaceDetector faceDetector;
        private IQRCodeScanner qrScanner;

        private float lastActivityTime;

        public KioskPresenceState State { get; private set; }
            = KioskPresenceState.Idle;

        public event Action Activated;
        public event Action BecameIdle;


        private void Awake()
        {
            faceDetector = faceDetectorSource as IFaceDetector;

            if (faceDetector == null)
            {
                Debug.LogError(
                    "[Kiosk Presence] Face Detector Source must implement IFaceDetector.",
                    this);
            }

            qrScanner = qrScannerSource as IQRCodeScanner;

            if (qrScanner == null)
            {
                Debug.LogError(
                    "[Kiosk Presence] QR Scanner Source must implement IQRCodeScanner.",
                    this);
            }
        }


        private void OnEnable()
        {
            if (faceDetector != null)
                faceDetector.FaceDetected += OnFaceDetected;

            if (qrScanner != null)
                qrScanner.QrCodeDetected += OnQrCodeDetected;
        }


        private void OnDisable()
        {
            if (faceDetector != null)
                faceDetector.FaceDetected -= OnFaceDetected;

            if (qrScanner != null)
                qrScanner.QrCodeDetected -= OnQrCodeDetected;
        }


        private void Update()
        {
            if (faceDetector != null && faceDetector.HasFace)
            {
                RegisterActivity();
                return;
            }

            if (State != KioskPresenceState.Active)
                return;

            if (Time.unscaledTime - lastActivityTime < inactivityTimeout)
                return;

            SetState(KioskPresenceState.Idle);
        }


        public void RegisterActivity()
        {
            lastActivityTime = Time.unscaledTime;

            if (State == KioskPresenceState.Idle)
                SetState(KioskPresenceState.Active);
        }


        private void OnFaceDetected()
        {
            RegisterActivity();
        }


        private void OnQrCodeDetected(string code)
        {
            Debug.Log("[Kiosk Presence] QR activity");

            RegisterActivity();
        }


        private void SetState(KioskPresenceState newState)
        {
            if (State == newState)
                return;

            State = newState;

            if (State == KioskPresenceState.Active)
            {
                Debug.Log("[Kiosk Presence] ACTIVE");
                Activated?.Invoke();
            }
            else
            {
                Debug.Log("[Kiosk Presence] IDLE");
                BecameIdle?.Invoke();
            }
        }
    }
}