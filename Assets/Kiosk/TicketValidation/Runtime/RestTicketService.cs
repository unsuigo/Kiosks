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

            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var request = new UnityWebRequest(
                    $"{baseUrl}/api/tickets/validate",
                    UnityWebRequest.kHttpVerbPOST);

                request.timeout = 3;

                byte[] body = Encoding.UTF8.GetBytes(json);

                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();

                request.SetRequestHeader(
                    "Content-Type",
                    "application/json");

                request.SetRequestHeader(
                    "Authorization",
                    "Bearer kiosk-test-token-123");

                Debug.Log($"HTTP attempt {attempt}/{maxAttempts}");

                yield return request.SendWebRequest();

                Debug.Log($"HTTP status: {request.responseCode}");
                Debug.Log($"Request result: {request.result}");
                Debug.Log($"Request error: {request.error}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response =
                        JsonUtility.FromJson<TicketValidationResponse>(
                            request.downloadHandler.text);

                    onSuccess?.Invoke(response);

                    yield break;
                }

                bool canRetry =
                    request.result == UnityWebRequest.Result.ConnectionError;

                if (canRetry && attempt < maxAttempts)
                {
                    Debug.Log(
                        "Temporary connection error. Retrying in 1 second...");

                    yield return new WaitForSecondsRealtime(1f);

                    continue;
                }

                onError?.Invoke(
                    $"Request failed. HTTP {request.responseCode}. " +
                    $"Error: {request.error}. " +
                    $"Body: {request.downloadHandler.text}");

                yield break;
            }
        }
    }
}