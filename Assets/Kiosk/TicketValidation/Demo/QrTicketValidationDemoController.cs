using Kiosk.QR;
using UnityEngine;

namespace Kiosk.TicketValidation
{
    public class QrTicketValidationDemoController : MonoBehaviour
    {
        [Header("QR Scanner")]
        [SerializeField] private MonoBehaviour qrScannerSource;

        [Header("Backend")]
        [SerializeField] private string baseUrl = "http://localhost:5123";

        private IQRCodeScanner qrScanner;
        private ITicketService ticketService;

        private bool isValidating;


        private void Awake()
        {
            qrScanner = qrScannerSource as IQRCodeScanner;

            if (qrScanner == null)
            {
                Debug.LogError(
                    "[Ticket Validation] QR Scanner Source must implement IQRCodeScanner.",
                    this);
            }

            ticketService = new RestTicketService(baseUrl);
        }


        private void OnEnable()
        {
            if (qrScanner == null)
                return;

            qrScanner.QrCodeDetected += OnQrCodeDetected;
        }


        private void OnDisable()
        {
            if (qrScanner == null)
                return;

            qrScanner.QrCodeDetected -= OnQrCodeDetected;
        }


        private void OnQrCodeDetected(string code)
        {
            if (isValidating)
            {
                Debug.Log(
                    $"QR ignored while another ticket is being validated: '{code}'");

                return;
            }

            Debug.Log($"QR received: '{code}'");

            isValidating = true;

            StartCoroutine(
                ticketService.ValidateTicket(
                    code,

                    response =>
                    {
                        isValidating = false;

                        Debug.Log(
                            $"TICKET RESULT: " +
                            $"valid={response.valid}, " +
                            $"message='{response.message}'");
                    },

                    error =>
                    {
                        isValidating = false;

                        Debug.LogError(
                            $"TICKET VALIDATION ERROR: {error}");
                    }));
        }
    }
}