using Kiosk.QR;
using UnityEngine;

namespace Kiosk.TicketValidation
{
    public class QrTicketValidationDemoController : MonoBehaviour
    {
        [SerializeField]
        private WebcamQrCodeScanner qrScanner;

        [SerializeField]
        private string baseUrl = "http://localhost:5123";

        private ITicketService ticketService;

        private bool isValidating;

        private void Awake()
        {
            ticketService = new RestTicketService(baseUrl);
        }

        private void OnEnable()
        {
            if (qrScanner != null)
            {
                qrScanner.QrCodeDetected += OnQrCodeDetected;
            }
        }

        private void OnDisable()
        {
            if (qrScanner != null)
            {
                qrScanner.QrCodeDetected -= OnQrCodeDetected;
            }
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
                            $"TICKET RESULT: valid={response.valid}, " +
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