using UnityEngine;
using UnityEngine.UI;

namespace Kiosk.QR.Demo
{
    [DisallowMultipleComponent]
    public sealed class QRCodeScannerDemoController : MonoBehaviour
    {
        [SerializeField] private WebcamQrCodeScanner scanner;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text decodedValueText;

        private void Awake()
        {
            if (scanner == null)
            {
                scanner = GetComponent<WebcamQrCodeScanner>();
            }

            if (startButton != null)
            {
                startButton.onClick.AddListener(StartScanner);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(StopScanner);
            }

            if (scanner != null)
            {
                scanner.QrCodeDetected += HandleQrCodeDetected;
                scanner.StatusChanged += HandleStatusChanged;
                SetStatus(scanner.State, "Ready.");
            }
            else
            {
                SetStatus(QRCodeScannerState.Error, "Scanner component is not configured.");
            }
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartScanner);
            }

            if (stopButton != null)
            {
                stopButton.onClick.RemoveListener(StopScanner);
            }

            if (scanner != null)
            {
                scanner.QrCodeDetected -= HandleQrCodeDetected;
                scanner.StatusChanged -= HandleStatusChanged;
            }
        }

        private void StartScanner()
        {
            scanner?.StartScanning();
        }

        private void StopScanner()
        {
            scanner?.StopScanning();
        }

        private void HandleQrCodeDetected(string value)
        {
            if (decodedValueText != null)
            {
                decodedValueText.text = value;
            }
        }

        private void HandleStatusChanged(QRCodeScannerState state, string message)
        {
            SetStatus(state, message);
        }

        private void SetStatus(QRCodeScannerState state, string message)
        {
            if (statusText != null)
            {
                statusText.text = $"{state}: {message}";
            }
        }
    }
}
