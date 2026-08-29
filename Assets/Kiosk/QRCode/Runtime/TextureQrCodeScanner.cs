using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Kiosk.QR
{
    [DisallowMultipleComponent]
    public sealed class TextureQrCodeScanner : MonoBehaviour, IQRCodeScanner
    {
        [Header("Scanning")]
        [Range(1f, 15f)]
        [SerializeField] private float scansPerSecond = 7f;

        [Min(0f)]
        [SerializeField] private float duplicateCooldownSeconds = 2f;

        [Min(0.5f)]
        [SerializeField] private float initializationTimeoutSeconds = 8f;

        [Min(1)]
        [SerializeField] private int decoderFailureLimit = 3;

        [SerializeField] private bool autoStart;

        [Header("Optional Preview")]
        [SerializeField] private RawImage preview;

        [Header("Diagnostics")]
        [SerializeField] private bool logStatusMessages = true;

        [SerializeField] private bool enableDetailedDiagnostics = true;

        [Tooltip("Disable this in production if QR payloads may contain sensitive data.")]
        [SerializeField] private bool logDecodedPayloads = true;

        [Min(1)]
        [SerializeField] private int diagnosticLogEveryNDecodes = 30;

        private IQRCodeDecoder decoder;
        private QRCodeDuplicateFilter duplicateFilter;

        private Texture sourceTexture;
        private Texture2D readbackTexture;

        private Color32[] pixelBuffer;
        private Task<DecodeOutcome> decodeTask;

        private int scanSession;
        private int consecutiveDecoderFailures;
        private int decodeAttempts;
        private int successfulDecodes;
        private int duplicatesSuppressed;

        private bool wantsToScan;

        private float initializationStartedAt;
        private float nextDecodeAt;

        private Texture originalPreviewTexture;
        private bool previewStateCaptured;


        public event Action<string> QrCodeDetected;
        public event Action<QRCodeScannerState, string> StatusChanged;


        public bool IsScanning => wantsToScan;

        public QRCodeScannerState State { get; private set; }
            = QRCodeScannerState.Stopped;

        public string LastError { get; private set; }
            = string.Empty;

        public string ActiveCameraName { get; }
        public IReadOnlyList<string> AvailableCameraNames { get; }

        public Texture SourceTexture => sourceTexture;


        public RawImage Preview
        {
            get => preview;
            set
            {
                if (preview == value)
                    return;

                RestorePreview();

                preview = value;

                if (sourceTexture != null)
                    AttachPreview();
            }
        }


        private void Awake()
        {
            decoder = new ZXingQRCodeDecoder();

            duplicateFilter =
                new QRCodeDuplicateFilter(duplicateCooldownSeconds);
        }


        private void Start()
        {
            if (autoStart)
                StartScanning();
        }


        private void Update()
        {
            CompleteDecodeIfReady();

            if (!wantsToScan)
                return;

            float now = Time.realtimeSinceStartup;

            if (State == QRCodeScannerState.Initializing)
            {
                UpdateInitialization(now);
                return;
            }

            if (State != QRCodeScannerState.Scanning)
                return;

            if (!IsSourceReady())
            {
                BeginWaitingForSource(
                    "Source texture is not ready.");
                return;
            }

            // If MediaPipe gives us a WebCamTexture,
            // only scan when a new camera frame exists.
            if (sourceTexture is WebCamTexture webcamTexture &&
                !webcamTexture.didUpdateThisFrame)
            {
                return;
            }

            if (decodeTask == null && now >= nextDecodeAt)
                CaptureAndDecode(now);
        }


        // ------------------------------------------------------------
        // External texture source
        // ------------------------------------------------------------

        public void SetSourceTexture(Texture texture)
        {
            if (sourceTexture == texture)
                return;

            RestorePreview();

            sourceTexture = texture;

            if (sourceTexture != null)
                AttachPreview();

            LogDiagnostic(
                sourceTexture != null
                    ? $"Source texture assigned: " +
                      $"'{sourceTexture.name}' " +
                      $"({sourceTexture.width}x{sourceTexture.height})."
                    : "Source texture cleared.");

            if (!wantsToScan)
                return;

            if (IsSourceReady())
            {
                nextDecodeAt = Time.realtimeSinceStartup;

                SetState(
                    QRCodeScannerState.Scanning,
                    $"Scanning external texture " +
                    $"{sourceTexture.width}x{sourceTexture.height}.");
            }
            else
            {
                BeginWaitingForSource(
                    "Waiting for external texture source.");
            }
        }


        public void ClearSourceTexture()
        {
            SetSourceTexture(null);
        }


        // ------------------------------------------------------------
        // Scanner lifecycle
        // ------------------------------------------------------------

        [ContextMenu("Start Scanning")]
        public void StartScanning()
        {
            if (wantsToScan &&
                (State == QRCodeScannerState.Initializing ||
                 State == QRCodeScannerState.Scanning))
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                Fail(
                    "The scanner component must be active and enabled " +
                    "before scanning can start.");

                return;
            }

            scanSession++;

            wantsToScan = true;
            LastError = string.Empty;

            consecutiveDecoderFailures = 0;

            duplicateFilter ??=
                new QRCodeDuplicateFilter(
                    duplicateCooldownSeconds);

            duplicateFilter.CooldownSeconds =
                duplicateCooldownSeconds;

            duplicateFilter.Reset();

            decodeAttempts = 0;
            successfulDecodes = 0;
            duplicatesSuppressed = 0;

            LogDiagnostic(
                $"Start requested. " +
                $"scanRate={scansPerSecond:0.##}/s, " +
                $"duplicateCooldown=" +
                $"{duplicateCooldownSeconds:0.##}s.");

            if (IsSourceReady())
            {
                nextDecodeAt = Time.realtimeSinceStartup;

                SetState(
                    QRCodeScannerState.Scanning,
                    $"Scanning external texture " +
                    $"{sourceTexture.width}x{sourceTexture.height}.");
            }
            else
            {
                BeginWaitingForSource(
                    "Waiting for external texture source.");
            }
        }


        [ContextMenu("Stop Scanning")]
        public void StopScanning()
        {
            if (!wantsToScan &&
                State == QRCodeScannerState.Stopped)
            {
                return;
            }

            wantsToScan = false;
            scanSession++;

            LogDiagnostic(
                $"Stop requested. " +
                $"Decode attempts={decodeAttempts}, " +
                $"successful decodes={successfulDecodes}, " +
                $"duplicates suppressed={duplicatesSuppressed}.");

            duplicateFilter?.Reset();

            SetState(
                QRCodeScannerState.Stopped,
                "QR scanning stopped.");

            // IMPORTANT:
            // We do NOT stop or destroy sourceTexture.
            // The camera belongs to MediaPipe / shared camera source.
        }


        private void OnDisable()
        {
            StopScanning();
        }


        private void OnDestroy()
        {
            StopScanning();

            RestorePreview();

            if (readbackTexture != null)
            {
                Destroy(readbackTexture);
                readbackTexture = null;
            }

            QrCodeDetected = null;
            StatusChanged = null;
        }


        private void OnValidate()
        {
            scansPerSecond =
                Mathf.Clamp(scansPerSecond, 1f, 15f);

            duplicateCooldownSeconds =
                Math.Max(0f, duplicateCooldownSeconds);

            initializationTimeoutSeconds =
                Math.Max(0.5f, initializationTimeoutSeconds);

            decoderFailureLimit =
                Math.Max(1, decoderFailureLimit);

            diagnosticLogEveryNDecodes =
                Math.Max(1, diagnosticLogEveryNDecodes);
        }


        // ------------------------------------------------------------
        // Initialization
        // ------------------------------------------------------------

        private void BeginWaitingForSource(string message)
        {
            initializationStartedAt =
                Time.realtimeSinceStartup;

            SetState(
                QRCodeScannerState.Initializing,
                message);
        }


        private void UpdateInitialization(float now)
        {
            if (IsSourceReady())
            {
                nextDecodeAt = now;

                SetState(
                    QRCodeScannerState.Scanning,
                    $"Scanning external texture " +
                    $"{sourceTexture.width}x{sourceTexture.height}.");

                return;
            }

            if (now - initializationStartedAt >
                initializationTimeoutSeconds)
            {
                Fail(
                    $"External texture source was not ready within " +
                    $"{initializationTimeoutSeconds:0.0} seconds.");
            }
        }


        private bool IsSourceReady()
        {
            if (sourceTexture == null)
                return false;

            if (sourceTexture.width <= 16 ||
                sourceTexture.height <= 16)
            {
                return false;
            }

            if (sourceTexture is WebCamTexture webcamTexture)
            {
                return webcamTexture.isPlaying &&
                       webcamTexture.width > 16 &&
                       webcamTexture.height > 16;
            }

            return true;
        }


        // ------------------------------------------------------------
        // Frame capture
        // ------------------------------------------------------------

        private void CaptureAndDecode(float now)
        {
            if (!TryReadSourcePixels(
                    out Color32[] capturedPixels,
                    out int width,
                    out int height,
                    out string error))
            {
                HandleDecoderFailure(error);
                return;
            }

            int capturedSession = scanSession;
            int capturedAttempt = ++decodeAttempts;

            IQRCodeDecoder capturedDecoder = decoder;

            nextDecodeAt =
                now + 1f / Math.Max(1f, scansPerSecond);

            decodeTask = Task.Run(() =>
            {
                try
                {
                    bool decoded =
                        capturedDecoder.TryDecode(
                            capturedPixels,
                            width,
                            height,
                            out string text);

                    return new DecodeOutcome(
                        capturedSession,
                        capturedAttempt,
                        decoded ? text : null,
                        null);
                }
                catch (Exception exception)
                {
                    return new DecodeOutcome(
                        capturedSession,
                        capturedAttempt,
                        null,
                        exception.Message);
                }
            });
        }


        private bool TryReadSourcePixels(
            out Color32[] pixels,
            out int width,
            out int height,
            out string error)
        {
            pixels = null;
            width = 0;
            height = 0;
            error = null;

            if (!IsSourceReady())
            {
                error = "Source texture is not ready.";
                return false;
            }

            width = sourceTexture.width;
            height = sourceTexture.height;

            try
            {
                // Best case for our current kiosk:
                // MediaPipe's webcam source is still a WebCamTexture,
                // but MediaPipe owns it.
                if (sourceTexture is WebCamTexture webcamTexture)
                {
                    pixelBuffer =
                        webcamTexture.GetPixels32(pixelBuffer);

                    pixels = pixelBuffer;
                    return true;
                }

                // Readable Texture2D can be read directly.
                if (sourceTexture is Texture2D texture2D &&
                    texture2D.isReadable)
                {
                    pixels = texture2D.GetPixels32();
                    return true;
                }

                // Generic fallback:
                // RenderTexture, non-readable Texture2D, etc.
                EnsureReadbackTexture(width, height);

                RenderTexture temporary =
                    RenderTexture.GetTemporary(
                        width,
                        height,
                        0,
                        RenderTextureFormat.ARGB32);

                RenderTexture previous =
                    RenderTexture.active;

                try
                {
                    Graphics.Blit(
                        sourceTexture,
                        temporary);

                    RenderTexture.active = temporary;

                    readbackTexture.ReadPixels(
                        new Rect(
                            0,
                            0,
                            width,
                            height),
                        0,
                        0,
                        false);

                    readbackTexture.Apply(
                        false,
                        false);

                    pixels =
                        readbackTexture.GetPixels32();

                    return true;
                }
                finally
                {
                    RenderTexture.active = previous;

                    RenderTexture.ReleaseTemporary(
                        temporary);
                }
            }
            catch (Exception exception)
            {
                error =
                    $"Could not read external texture: " +
                    $"{exception.Message}";

                return false;
            }
        }


        private void EnsureReadbackTexture(
            int width,
            int height)
        {
            if (readbackTexture != null &&
                readbackTexture.width == width &&
                readbackTexture.height == height)
            {
                return;
            }

            if (readbackTexture != null)
                Destroy(readbackTexture);

            readbackTexture =
                new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false);
        }


        // ------------------------------------------------------------
        // Decode result
        // ------------------------------------------------------------

        private void CompleteDecodeIfReady()
        {
            Task<DecodeOutcome> completedTask =
                decodeTask;

            if (completedTask == null ||
                !completedTask.IsCompleted)
            {
                return;
            }

            decodeTask = null;

            if (completedTask.IsCanceled)
                return;

            if (completedTask.IsFaulted)
            {
                HandleDecoderFailure(
                    completedTask.Exception?
                        .GetBaseException()
                        .Message
                    ?? "Unknown decoder failure.");

                return;
            }

            DecodeOutcome outcome =
                completedTask.Result;

            if (!wantsToScan ||
                outcome.Session != scanSession)
            {
                return;
            }

            if (!string.IsNullOrEmpty(outcome.Error))
            {
                HandleDecoderFailure(
                    outcome.Error);

                return;
            }

            consecutiveDecoderFailures = 0;

            if (string.IsNullOrEmpty(outcome.Text))
            {
                if (outcome.Attempt %
                    Math.Max(
                        1,
                        diagnosticLogEveryNDecodes) == 0)
                {
                    LogDiagnostic(
                        $"Decode attempt " +
                        $"#{outcome.Attempt}: " +
                        $"no readable QR code in " +
                        $"{sourceTexture?.width ?? 0}x" +
                        $"{sourceTexture?.height ?? 0} frame.");
                }

                return;
            }

            successfulDecodes++;

            duplicateFilter.CooldownSeconds =
                duplicateCooldownSeconds;

            if (!duplicateFilter.ShouldEmit(
                    outcome.Text,
                    Time.realtimeSinceStartupAsDouble))
            {
                duplicatesSuppressed++;

                if (duplicatesSuppressed == 1 ||
                    duplicatesSuppressed %
                    Math.Max(
                        1,
                        diagnosticLogEveryNDecodes) == 0)
                {
                    LogDiagnostic(
                        logDecodedPayloads
                            ? $"Duplicate QR suppressed: " +
                              $"'{outcome.Text}' " +
                              $"(total suppressed=" +
                              $"{duplicatesSuppressed})."
                            : $"Duplicate QR suppressed " +
                              $"(total suppressed=" +
                              $"{duplicatesSuppressed}, " +
                              $"payload logging disabled).");
                }

                return;
            }

            LogDiagnostic(
                logDecodedPayloads
                    ? $"QR detected on attempt " +
                      $"#{outcome.Attempt}: " +
                      $"'{outcome.Text}'."
                    : $"QR detected on attempt " +
                      $"#{outcome.Attempt} " +
                      $"(payload logging disabled).");

            try
            {
                QrCodeDetected?.Invoke(
                    outcome.Text);
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception,
                    this);
            }
        }


        // ------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------

        private void AttachPreview()
        {
            if (preview == null ||
                sourceTexture == null)
            {
                return;
            }

            if (!previewStateCaptured)
            {
                originalPreviewTexture =
                    preview.texture;

                previewStateCaptured = true;
            }

            preview.texture =
                sourceTexture;
        }


        private void RestorePreview()
        {
            if (!previewStateCaptured ||
                preview == null)
            {
                previewStateCaptured = false;
                return;
            }

            preview.texture =
                originalPreviewTexture;

            previewStateCaptured = false;
        }


        // ------------------------------------------------------------
        // Errors / state / diagnostics
        // ------------------------------------------------------------

        private void HandleDecoderFailure(
            string message)
        {
            if (!wantsToScan)
                return;

            consecutiveDecoderFailures++;

            LastError =
                $"QR decoder error " +
                $"({consecutiveDecoderFailures}/" +
                $"{decoderFailureLimit}): " +
                $"{message}";

            if (consecutiveDecoderFailures >=
                decoderFailureLimit)
            {
                Fail(LastError);
                return;
            }

            Debug.LogWarning(
                $"[QR Scanner] {LastError}",
                this);

            StatusChanged?.Invoke(
                State,
                LastError);
        }


        private void Fail(string message)
        {
            wantsToScan = false;
            scanSession++;

            LastError = message;

            SetState(
                QRCodeScannerState.Error,
                message);

            // Do NOT touch sourceTexture.
            // It belongs to the shared camera owner.
        }


        private void LogDiagnostic(
            string message)
        {
            if (!enableDetailedDiagnostics)
                return;

            Debug.Log(
                $"[QR Scanner] {message}",
                this);
        }


        private void SetState(
            QRCodeScannerState state,
            string message)
        {
            State = state;

            if (logStatusMessages)
            {
                if (state ==
                    QRCodeScannerState.Error)
                {
                    Debug.LogError(
                        $"[QR Scanner] {message}",
                        this);
                }
                else
                {
                    Debug.Log(
                        $"[QR Scanner] {message}",
                        this);
                }
            }

            StatusChanged?.Invoke(
                state,
                message);
        }


        private readonly struct DecodeOutcome
        {
            public DecodeOutcome(
                int session,
                int attempt,
                string text,
                string error)
            {
                Session = session;
                Attempt = attempt;
                Text = text;
                Error = error;
            }

            public int Session { get; }
            public int Attempt { get; }
            public string Text { get; }
            public string Error { get; }
        }
    }
}