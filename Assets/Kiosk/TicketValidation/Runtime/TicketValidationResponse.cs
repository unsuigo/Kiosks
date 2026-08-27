using System;

namespace Kiosk.TicketValidation
{
    [Serializable]
    public class TicketValidationResponse
    {
        public bool valid;
        public string message;
    }
}