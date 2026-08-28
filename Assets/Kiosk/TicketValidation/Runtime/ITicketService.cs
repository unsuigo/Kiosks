using System;
using System.Collections;

namespace Kiosk.TicketValidation
{
    public interface ITicketService
    {
        IEnumerator ValidateTicket(
            string code,
            Action<TicketValidationResponse> onSuccess,
            Action<string> onError);
    }
}