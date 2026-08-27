using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Kiosk.QR
{
    [DisallowMultipleComponent]
    public sealed class WebcamQrCodeScanner : MonoBehaviour, IQRCodeScanner
    {
        [Header("Camera Selection")]
        [Tooltip("Exact webcam name. Leave empty to use Camera Index.")]
        [SerializeField] private string cameraName = string.Empty;
        [Min(0)]
        [SerializeField] private int cameraIndex;

        [Header("Camera Request")]
        [Min(16)]
        [SerializeField] private int requestedWidth = 1280;
        [Min(16)]
        [SerializeField] private int requestedHeight = 720;
        [Range(1, 60)]
        [SerializeField] private int requestedFps = 30;

        [Header("Scanning")]
        [Range(1f, 15f)]
        [SerializeField] private float scansPerSecond = 7f;
        [Min(0f)]
        [SerializeField] private float duplicateCooldownSeconds = 2f;
        [Min(0.5f)]
        [SerializeField] private float initializationTimeoutSeconds = 8f;
        [Min(0.5f)]
        [SerializeField] private float cameraFrameTimeoutSeconds = 5f;
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

        private readonly List<string> availableCameraNames = new List<string>();
        private IQRCodeDecoder decoder;
        private QRCodeDuplicateFilter duplicateFilter;
        private WebCamTexture webcamTexture;
        private Color32[] pixelBuffer;
        private Task<DecodeOutcome> decodeTask;
        private int scanSession;
        private int consecutiveDecoderFailures;
        private bool wantsToScan;
        private float initializationStartedAt;
        private float lastCameraFrameAt;
        private float nextDecodeAt;
        private float nextDeviceCheckAt;
        private Texture originalPreviewTexture;
        private Rect originalPreviewUvRect;
        private Quaternion originalPreviewRotation;
        private bool previewStateCaptured;
        private int decodeAttempts;
        private int successfulDecodes;
        private int duplicatesSuppressed;

        public event Action<string> QrCodeDetected;
        public event Action<QRCodeScannerState, string> StatusChanged;

        public bool IsScanning => wantsToScan;
        public QRCodeScannerState State { get; private set; } = QRCodeScannerState.Stopped;
        public string LastError { get; private set; } = string.Empty;
        public string ActiveCameraName { get; private set; } = string.Empty;

        public IReadOnlyList<string> AvailableCameraNames
        {
            get
            {
                RefreshCameraNames();
                return availableCameraNames;
            }
        }

        public RawImage Preview
        {
            get => preview;
            set => preview = value;
        }

        private void Awake()
        {
            decoder = new ZXingQRCodeDecoder();
            duplicateFilter = new QRCodeDuplicateFilter(duplicateCooldownSeconds);
            RefreshCameraNames();
        }

        private void Start()
        {
            if (autoStart)
            {
                StartScanning();
            }
        }

        private void Update()
        {
            CompleteDecodeIfReady();

            if (!wantsToScan)
            {
                return;
            }

            if (webcamTexture == null)
            {
                Fail("The webcam texture is no longer available.");
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (State == QRCodeScannerState.Initializing)
            {
                UpdateInitialization(now);
                return;
            }

            if (State != QRCodeScannerState.Scanning)
            {
                return;
            }

            if (!webcamTexture.isPlaying)
            {
                Fail($"Camera '{ActiveCameraName}' stopped unexpectedly.");
                return;
            }

            if (webcamTexture.didUpdateThisFrame)
            {
                lastCameraFrameAt = now;
                UpdatePreviewOrientation();
            }

            if (now >= nextDeviceCheckAt)
            {
                nextDeviceCheckAt = now + 1f;
                if (!IsActiveCameraConnected())
                {
                    Fail($"Camera '{ActiveCameraName}' was removed or disconnected.");
                    return;
                }
            }

            if (now - lastCameraFrameAt > cameraFrameTimeoutSeconds)
            {
                Fail($"Camera '{ActiveCameraName}' stopped delivering frames.");
                return;
            }

            if (webcamTexture.didUpdateThisFrame && decodeTask == null && now >= nextDecodeAt)
            {
                CaptureAndDecode(now);
            }
        }

        public void SelectCamera(int index)
        {
            EnsureStoppedBeforeCameraChange();
            cameraName = string.Empty;
            cameraIndex = Math.Max(0, index);
        }

        public void SelectCamera(string exactName)
        {
            EnsureStoppedBeforeCameraChange();
            cameraName = exactName ?? string.Empty;
        }

        [ContextMenu("Start Scanning")]
        public void StartScanning()
        {
            if (wantsToScan && (State == QRCodeScannerState.Initializing || State == QRCodeScannerState.Scanning))
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                Fail("The scanner component must be active and enabled before scanning can start.");
                return;
            }

            StopCameraResources();
            scanSession++;
            wantsToScan = true;
            LastError = string.Empty;
            ActiveCameraName = string.Empty;
            consecutiveDecoderFailures = 0;
            duplicateFilter ??= new QRCodeDuplicateFilter(duplicateCooldownSeconds);
            duplicateFilter.CooldownSeconds = duplicateCooldownSeconds;
            duplicateFilter.Reset();
            decodeAttempts = 0;
            successfulDecodes = 0;
            duplicatesSuppressed = 0;
            LogDiagnostic(
                $"Start requested. Camera name='{cameraName}', index={cameraIndex}, " +
                $"request={requestedWidth}x{requestedHeight}@{requestedFps}, " +
                $"scanRate={scansPerSecond:0.##}/s, duplicateCooldown={duplicateCooldownSeconds:0.##}s.");

            WebCamDevice[] devices = WebCamTexture.devices;
            RefreshCameraNames(devices);
            LogDiagnostic($"Unity reports {availableCameraNames.Count} camera(s): {DescribeAvailableCameras()}.");
            if (devices == null || devices.Length == 0)
            {
                Fail("No webcam is connected or available to Unity.");
                return;
            }

            if (!TrySelectDevice(devices, out WebCamDevice selectedDevice, out string selectionError))
            {
                Fail(selectionError);
                return;
            }

            try
            {
                ActiveCameraName = selectedDevice.name;
                LogDiagnostic($"Selected camera '{ActiveCameraName}'. Creating WebCamTexture.");
                webcamTexture = new WebCamTexture(ActiveCameraName, requestedWidth, requestedHeight, requestedFps);
                AttachPreview();
                initializationStartedAt = Time.realtimeSinceStartup;
                lastCameraFrameAt = initializationStartedAt;
                nextDeviceCheckAt = initializationStartedAt + 1f;
                webcamTexture.Play();
                SetState(QRCodeScannerState.Initializing, $"Initializing camera '{ActiveCameraName}'.");
            }
            catch (Exception exception)
            {
                Fail($"Could not initialize camera '{ActiveCameraName}': {exception.Message}");
            }
        }

        [ContextMenu("Stop Scanning")]
        public void StopScanning()
        {
            if (!wantsToScan && State == QRCodeScannerState.Stopped && webcamTexture == null)
            {
                return;
            }

            wantsToScan = false;
            scanSession++;
            LogDiagnostic(
                $"Stop requested. Decode attempts={decodeAttempts}, successful decodes={successfulDecodes}, " +
                $"duplicates suppressed={duplicatesSuppressed}.");
            StopCameraResources();
            duplicateFilter?.Reset();
            SetState(QRCodeScannerState.Stopped, "QR scanning stopped.");
        }

        private void OnDisable()
        {
            StopScanning();
        }

        private void OnDestroy()
        {
            StopScanning();
            QrCodeDetected = null;
            StatusChanged = null;
        }

        private void OnValidate()
        {
            cameraIndex = Math.Max(0, cameraIndex);
            requestedWidth = Math.Max(16, requestedWidth);
            requestedHeight = Math.Max(16, requestedHeight);
            requestedFps = Mathf.Clamp(requestedFps, 1, 60);
            scansPerSecond = Mathf.Clamp(scansPerSecond, 1f, 15f);
            duplicateCooldownSeconds = Math.Max(0f, duplicateCooldownSeconds);
            initializationTimeoutSeconds = Math.Max(0.5f, initializationTimeoutSeconds);
            cameraFrameTimeoutSeconds = Math.Max(0.5f, cameraFrameTimeoutSeconds);
            decoderFailureLimit = Math.Max(1, decoderFailureLimit);
            diagnosticLogEveryNDecodes = Math.Max(1, diagnosticLogEveryNDecodes);
        }

        private void UpdateInitialization(float now)
        {
            if (!webcamTexture.isPlaying)
            {
                Fail($"Camera '{ActiveCameraName}' failed to start.");
                return;
            }

            if (webcamTexture.didUpdateThisFrame && webcamTexture.width > 16 && webcamTexture.height > 16)
            {
                lastCameraFrameAt = now;
                nextDecodeAt = now;
                UpdatePreviewOrientation();
                SetState(
                    QRCodeScannerState.Scanning,
                    $"Scanning with camera '{ActiveCameraName}' at {webcamTexture.width}x{webcamTexture.height}.");
                return;
            }

            if (now - initializationStartedAt > initializationTimeoutSeconds)
            {
                Fail($"Camera '{ActiveCameraName}' did not initialize within {initializationTimeoutSeconds:0.0} seconds.");
            }
        }

        private void CaptureAndDecode(float now)
        {
            int width = webcamTexture.width;
            int height = webcamTexture.height;
            if (width <= 16 || height <= 16)
            {
                return;
            }

            try
            {
                pixelBuffer = webcamTexture.GetPixels32(pixelBuffer);
            }
            catch (Exception exception)
            {
                Fail($"Could not read a frame from camera '{ActiveCameraName}': {exception.Message}");
                return;
            }

            Color32[] capturedPixels = pixelBuffer;
            int capturedSession = scanSession;
            int capturedAttempt = ++decodeAttempts;
            IQRCodeDecoder capturedDecoder = decoder;
            nextDecodeAt = now + 1f / Math.Max(1f, scansPerSecond);

            decodeTask = Task.Run(() =>
            {
                try
                {
                    bool decoded = capturedDecoder.TryDecode(capturedPixels, width, height, out string text);
                    return new DecodeOutcome(capturedSession, capturedAttempt, decoded ? text : null, null);
                }
                catch (Exception exception)
                {
                    return new DecodeOutcome(capturedSession, capturedAttempt, null, exception.Message);
                }
            });
        }

        private void CompleteDecodeIfReady()
        {
            Task<DecodeOutcome> completedTask = decodeTask;
            if (completedTask == null || !completedTask.IsCompleted)
            {
                return;
            }

            decodeTask = null;
            if (completedTask.IsCanceled)
            {
                return;
            }

            if (completedTask.IsFaulted)
            {
                HandleDecoderFailure(completedTask.Exception?.GetBaseException().Message ?? "Unknown decoder failure.");
                return;
            }

            DecodeOutcome outcome = completedTask.Result;
            if (!wantsToScan || outcome.Session != scanSession)
            {
                return;
            }

            if (!string.IsNullOrEmpty(outcome.Error))
            {
                HandleDecoderFailure(outcome.Error);
                return;
            }

            consecutiveDecoderFailures = 0;
            if (string.IsNullOrEmpty(outcome.Text))
            {
                if (outcome.Attempt % Math.Max(1, diagnosticLogEveryNDecodes) == 0)
                {
                    LogDiagnostic(
                        $"Decode attempt #{outcome.Attempt}: no readable QR code in " +
                        $"{webcamTexture?.width ?? 0}x{webcamTexture?.height ?? 0} frame.");
                }

                return;
            }

            successfulDecodes++;
            duplicateFilter.CooldownSeconds = duplicateCooldownSeconds;
            if (!duplicateFilter.ShouldEmit(outcome.Text, Time.realtimeSinceStartupAsDouble))
            {
                duplicatesSuppressed++;
                if (duplicatesSuppressed == 1 ||
                    duplicatesSuppressed % Math.Max(1, diagnosticLogEveryNDecodes) == 0)
                {
                    LogDiagnostic(
                        logDecodedPayloads
                            ? $"Duplicate QR suppressed: '{outcome.Text}' (total suppressed={duplicatesSuppressed})."
                            : $"Duplicate QR suppressed (total suppressed={duplicatesSuppressed}, payload logging disabled).");
                }

                return;
            }

            LogDiagnostic(
                logDecodedPayloads
                    ? $"QR detected on attempt #{outcome.Attempt}: '{outcome.Text}'."
                    : $"QR detected on attempt #{outcome.Attempt} (payload logging disabled).");

            try
            {
                QrCodeDetected?.Invoke(outcome.Text);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void HandleDecoderFailure(string message)
        {
            if (!wantsToScan)
            {
                return;
            }

            consecutiveDecoderFailures++;
            LastError = $"QR decoder error ({consecutiveDecoderFailures}/{decoderFailureLimit}): {message}";
            if (consecutiveDecoderFailures >= decoderFailureLimit)
            {
                Fail(LastError);
                return;
            }

            Debug.LogWarning($"[QR Scanner] {LastError}", this);
            StatusChanged?.Invoke(State, LastError);
        }

        private bool TrySelectDevice(WebCamDevice[] devices, out WebCamDevice selectedDevice, out string error)
        {
            if (!string.IsNullOrWhiteSpace(cameraName))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (string.Equals(devices[i].name, cameraName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedDevice = devices[i];
                        error = null;
                        return true;
                    }
                }

                selectedDevice = default;
                error = $"Configured camera '{cameraName}' was not found.";
                return false;
            }

            if (cameraIndex < 0 || cameraIndex >= devices.Length)
            {
                selectedDevice = default;
                error = $"Camera index {cameraIndex} is out of range. Available camera count: {devices.Length}.";
                return false;
            }

            selectedDevice = devices[cameraIndex];
            error = null;
            return true;
        }

        private bool IsActiveCameraConnected()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            RefreshCameraNames(devices);
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i].name, ActiveCameraName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshCameraNames()
        {
            RefreshCameraNames(WebCamTexture.devices);
        }

        private void RefreshCameraNames(WebCamDevice[] devices)
        {
            availableCameraNames.Clear();
            if (devices == null)
            {
                return;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                availableCameraNames.Add(devices[i].name);
            }
        }

        private void AttachPreview()
        {
            if (preview == null)
            {
                return;
            }

            originalPreviewTexture = preview.texture;
            originalPreviewUvRect = preview.uvRect;
            originalPreviewRotation = preview.rectTransform.localRotation;
            previewStateCaptured = true;
            preview.texture = webcamTexture;
        }

        private void UpdatePreviewOrientation()
        {
            if (preview == null || webcamTexture == null)
            {
                return;
            }

            preview.uvRect = webcamTexture.videoVerticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            preview.rectTransform.localRotation =
                originalPreviewRotation * Quaternion.Euler(0f, 0f, -webcamTexture.videoRotationAngle);
        }

        private void RestorePreview()
        {
            if (!previewStateCaptured || preview == null)
            {
                previewStateCaptured = false;
                return;
            }

            preview.texture = originalPreviewTexture;
            preview.uvRect = originalPreviewUvRect;
            preview.rectTransform.localRotation = originalPreviewRotation;
            previewStateCaptured = false;
        }

        private void StopCameraResources()
        {
            RestorePreview();
            if (webcamTexture != null)
            {
                if (webcamTexture.isPlaying)
                {
                    webcamTexture.Stop();
                }

                Destroy(webcamTexture);
                webcamTexture = null;
            }

            pixelBuffer = null;
            ActiveCameraName = string.Empty;
        }

        private void Fail(string message)
        {
            wantsToScan = false;
            scanSession++;
            LastError = message;
            StopCameraResources();
            SetState(QRCodeScannerState.Error, message);
        }

        private string DescribeAvailableCameras()
        {
            if (availableCameraNames.Count == 0)
            {
                return "<none>";
            }

            var descriptions = new string[availableCameraNames.Count];
            for (int i = 0; i < availableCameraNames.Count; i++)
            {
                descriptions[i] = $"[{i}] {availableCameraNames[i]}";
            }

            return string.Join(" | ", descriptions);
        }

        private void LogDiagnostic(string message)
        {
            if (enableDetailedDiagnostics)
            {
                Debug.Log($"[QR Scanner] {message}", this);
            }
        }

        private void SetState(QRCodeScannerState state, string message)
        {
            State = state;
            if (logStatusMessages)
            {
                if (state == QRCodeScannerState.Error)
                {
                    Debug.LogError($"[QR Scanner] {message}", this);
                }
                else
                {
                    Debug.Log($"[QR Scanner] {message}", this);
                }
            }

            StatusChanged?.Invoke(state, message);
        }

        private void EnsureStoppedBeforeCameraChange()
        {
            if (wantsToScan || State == QRCodeScannerState.Initializing || State == QRCodeScannerState.Scanning)
            {
                throw new InvalidOperationException("Stop QR scanning before changing the selected camera.");
            }
        }

        private readonly struct DecodeOutcome
        {
            public DecodeOutcome(int session, int attempt, string text, string error)
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
