using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kiosk.TicketValidation
{
    public class RestTicketService : ITicketService
    {
        private readonly string baseUrl;

        public RestTicketService(string baseUrl)
        {
            this.baseUrl = baseUrl;
        }

        public IEnumerator ValidateTicket(
            string code,
            Action<TicketValidationResponse> onSuccess,
            Action<string> onError)
        {
            var requestData = new TicketValidationRequest
            {
                code = code
            };

            string json = JsonUtility.ToJson(requestData);

            using var request = new UnityWebRequest(
                $"{baseUrl}/api/tickets/validate",
                UnityWebRequest.kHttpVerbPOST);

            byte[] body = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type",
                "application/json");

            yield return request.SendWebRequest();
            Debug.Log($"HTTP status: {request.responseCode}");
            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"HTTP error: {request.responseCode} - {request.error}");

                yield break;
            }

            var response =
                JsonUtility.FromJson<TicketValidationResponse>(
                    request.downloadHandler.text);

            onSuccess?.Invoke(response);
        }
    }
}