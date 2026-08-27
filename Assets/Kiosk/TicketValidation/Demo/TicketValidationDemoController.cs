using UnityEngine;

namespace Kiosk.TicketValidation
{
    public class TicketValidationDemoController : MonoBehaviour
    {
        [SerializeField]
        private string baseUrl = "http://localhost:5123";

        [SerializeField]
        private string testCode = "2660488771";

        private ITicketService ticketService;

        private void Awake()
        {
            ticketService = new RestTicketService(baseUrl);
        }

        [ContextMenu("Validate Test Ticket")]
        public void ValidateTestTicket()
        {
            Debug.Log($"Validating ticket: '{testCode}'");

            StartCoroutine(
                ticketService.ValidateTicket(
                    testCode,
                    response =>
                    {
                        Debug.Log(
                            $"SUCCESS: valid={response.valid}, " +
                            $"message='{response.message}'");
                    },
                    error =>
                    {
                        Debug.LogError($"ERROR: {error}");
                    }));
        }
    }
}