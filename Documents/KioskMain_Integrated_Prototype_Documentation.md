# KioskMain — Integrated Camera, Face Detection, QR and Ticket Validation

## 1. Purpose

This document records the current state of the kiosk prototype after combining previously separate face-detection and QR/ticket-validation demos into a single `KioskMain` scene.

The prototype is a **working technical / interview-preparation implementation**, not a production-ready kiosk system.

The key integration goal has been achieved:

- one physical webcam;
- one MediaPipe-owned camera source;
- face detection from MediaPipe;
- QR decoding from the same camera texture through ZXing;
- QR events through `IQRCodeScanner`;
- ticket validation through a local ASP.NET Core REST API;
- SQLite lookup through Entity Framework Core.

---

## 2. Verified End-to-End Architecture

```text
                         Physical Webcam
                               |
                               v
                      MediaPipe ImageSource
                               |
                      Shared camera Texture
                        /                 \
                       /                   \
                      v                     v
            KioskFaceDetectorRunner   TextureQrCodeScanner
                  |                        |
                  |                        v
                  |                  ZXing QR decoder
                  |                        |
                  |                 QrCodeDetected
                  |                        |
                  |                        v
                  |             QrTicketValidationDemoController
                  |                        |
                  |                        v
                  |                   ITicketService
                  |                        |
                  |                        v
                  |                 RestTicketService
                  |                        |
                  |                        v
                  |                ASP.NET Core REST API
                  |                        |
                  |                        v
                  |                Entity Framework Core
                  |                        |
                  |                        v
                  |                     SQLite
                  |
                  v
            FaceDetected / FaceLost
```

The same webcam is no longer opened independently by MediaPipe and the QR scanner.

---

## 3. Current `KioskMain` Scene Structure

```text
KioskMain
├── Main Camera
├── Directional Light
├── Main Canvas
│   └── MediaPipe preview / face annotations
├── EventSystem
├── Solution
│   └── KioskFaceDetectorRunner
├── QR Scanner
│   └── TextureQrCodeScanner
├── Canvas
│   ├── Background
│   ├── Camera Preview
│   ├── Title
│   ├── Status
│   ├── Last Decoded QR
│   ├── Start Scan Button
│   ├── Stop Scan Button
│   └── QRCodeScannerDemoController
├── Ticket Validation Demo
│   └── QrTicketValidationDemoController
└── KioskCameraBridge
```

Important:

- there is one `EventSystem`;
- multiple Canvas objects are acceptable;
- `Main Canvas` belongs to the MediaPipe-derived UI;
- the second `Canvas` is currently QR demo/debug UI;
- `QRCodeScannerDemoController` and `TextureQrCodeScanner` were deliberately separated onto different GameObjects.

---

## 4. Camera Ownership

### Old design

```text
WebcamQrCodeScanner
→ creates WebCamTexture
→ starts webcam
→ captures frames
→ ZXing
```

MediaPipe also opened the webcam separately.

### Current design

MediaPipe owns the camera:

```text
MediaPipe ImageSource
→ starts webcam
→ exposes current Texture
```

`TextureQrCodeScanner` receives an existing `Texture` and does not own the webcam.

---

## 5. MediaPipe Face Detection

Current tested configuration:

```text
Delegate                  CPU
Image Read Mode           CPUAsync
Model                     BlazeFace short-range
Running Mode              LIVE_STREAM
MinDetectionConfidence    0.5
MinSuppressionThreshold   0.3
NumFaces                   3
```

`KioskFaceDetectorRunner` provides:

- face detection;
- annotation drawing;
- `HasFace`;
- `FaceDetected`;
- `FaceLost`;
- access to the current shared camera texture.

Conceptually:

```csharp
public Texture CurrentCameraTexture
{
    get
    {
        var imageSource = ImageSourceProvider.ImageSource;

        if (imageSource == null || !imageSource.isPrepared)
            return null;

        return imageSource.GetCurrentTexture();
    }
}
```

---

## 6. Face-Loss Hysteresis

Direct frame-by-frame `face/no face` caused flicker because individual MediaPipe frames can miss a face.

Current behavior:

```text
Face appears
→ FaceDetected immediately

Individual missed frames
→ ignored

Face continuously absent for ~2 seconds
→ FaceLost
```

This is sensor-level filtering.

---

## 7. Common QR Scanner Interface

The common abstraction represents QR-scanning behavior rather than webcam ownership:

```csharp
public interface IQRCodeScanner
{
    event Action<string> QrCodeDetected;
    event Action<QRCodeScannerState, string> StatusChanged;

    bool IsScanning { get; }
    QRCodeScannerState State { get; }
    string LastError { get; }

    void StartScanning();
    void StopScanning();
}
```

Shared scanner states:

```text
Stopped
Initializing
Scanning
Error
```

---

## 8. Two QR Implementations

### `WebcamQrCodeScanner`

Kept for the standalone QR demo/reference scene.

Responsibilities:

- owns `WebCamTexture`;
- camera selection;
- starts/stops webcam;
- frame capture;
- ZXing decoding;
- duplicate suppression;
- events/status.

### `TextureQrCodeScanner`

Used by `KioskMain`.

Responsibilities:

- accepts an external `Texture`;
- does not create or stop the webcam;
- reads pixels from the shared camera texture;
- ZXing decoding;
- `QrCodeDetected`;
- duplicate suppression;
- scanner state;
- optional preview;
- diagnostics.

---

## 9. `KioskCameraBridge`

The bridge connects the MediaPipe-owned camera texture to the QR scanner:

```text
KioskFaceDetectorRunner.CurrentCameraTexture
                    |
                    v
            KioskCameraBridge
                    |
                    v
 TextureQrCodeScanner.SetSourceTexture(texture)
```

Verified texture size:

```text
1280 x 720
```

Observed log:

```text
[Kiosk Camera] Shared texture assigned: 1280x720
```

For manual UI testing, automatic QR start can remain disabled so Start/Stop buttons control scanning.

---

## 10. QR Demo UI Controller

`QRCodeScannerDemoController` is now UI-only and is attached to the QR `Canvas`.

Responsibilities:

- Start button;
- Stop button;
- state text;
- last decoded QR text.

It references a `MonoBehaviour` and casts it to `IQRCodeScanner`, allowing the same controller to work with either scanner implementation.

Current `KioskMain` reference:

```text
Canvas
└── QRCodeScannerDemoController
    └── Scanner → QR Scanner (TextureQrCodeScanner)
```

---

## 11. Verified Shared-Camera QR Test

Test QR:

```text
2660488771
```

Observed:

```text
[QR Scanner] Scanning external texture 1280x720.
[QR Scanner] QR detected ... '2660488771'
```

Current tested settings:

```text
Scans Per Second              7
Duplicate Cooldown Seconds    2
Initialization Timeout        8
Decoder Failure Limit         3
Auto Start                    false
```

Duplicate suppression also works.

---

## 12. Simultaneous Face + QR

Verified:

```text
MediaPipe:
FaceDetected / FaceLost

while simultaneously:

ZXing:
QR detected
```

Stopping QR scanning does not stop MediaPipe face detection or the physical camera.

---

## 13. Ticket Validation Integration

`QrTicketValidationDemoController` also accepts the scanner through an Inspector `MonoBehaviour` reference and casts it to `IQRCodeScanner`.

Correct reference:

```text
Ticket Validation Demo
└── QrTicketValidationDemoController
    ├── Qr Scanner Source → QR Scanner (TextureQrCodeScanner)
    └── Base Url → http://localhost:5123
```

Important lesson: if a GameObject has several `MonoBehaviour` components, a generic `MonoBehaviour` field can receive the wrong component. The reference must resolve specifically to `TextureQrCodeScanner`.

---

## 14. QR → REST Flow

```text
TextureQrCodeScanner
        |
        v
QrCodeDetected(string)
        |
        v
QrTicketValidationDemoController
        |
        v
ITicketService.ValidateTicket(...)
        |
        v
RestTicketService
```

The controller uses `isValidating` to avoid overlapping validation requests.

---

## 15. REST Client

Endpoint:

```http
POST http://localhost:5123/api/tickets/validate
```

Request:

```json
{
  "code": "2660488771"
}
```

Prototype authorization:

```http
Authorization: Bearer kiosk-test-token-123
```

Successful response:

```json
{
  "valid": true,
  "message": "Ticket is valid"
}
```

The token is hardcoded only for the prototype.

---

## 16. Timeout / Retry

Current client behavior:

```text
Request timeout: 3 seconds
Maximum attempts: 2
Retry delay: 1 second
Retry: connection errors
```

A fresh `UnityWebRequest` is created for each attempt.

Production note: retries for state-changing POST operations require idempotency protection.

---

## 17. Local Backend

Backend:

```text
D:\Development\Unity\Tests\Kiosk_test\BackendMock
```

Git Bash:

```bash
cd /d/Development/Unity/Tests/Kiosk_test/BackendMock
```

Start:

```bash
dotnet run
```

Server:

```text
http://localhost:5123
```

Health:

```text
http://localhost:5123/health
```

Expected:

```json
{"status":"ok"}
```

---

## 18. Backend / Database Stack

```text
.NET 8
ASP.NET Core Minimal API
Entity Framework Core
SQLite
```

Database:

```text
BackendMock/kiosk.db
```

Entity:

```text
Ticket
├── Id
├── Code
└── IsActive
```

Test data:

```text
2660488771 → active / valid
5555555555 → active / valid
9999999999 → inactive
unknown    → not found
```

---

## 19. Verified Full Integrated Test

Verified pipeline:

```text
Physical QR
→ one webcam
→ MediaPipe ImageSource
→ shared Texture
→ TextureQrCodeScanner
→ ZXing
→ QrCodeDetected
→ QrTicketValidationDemoController
→ RestTicketService
→ HTTP POST
→ ASP.NET Core
→ EF Core
→ SQLite
→ HTTP 200
→ JSON
→ Unity
```

Observed:

```text
QR received: '2660488771'
HTTP attempt 1/2
HTTP status: 200
Request result: Success
TICKET RESULT: valid=True, message='Ticket is valid'
```

Face detection continues working at the same time.

**Status: integrated prototype working.**

---

## 20. Current Minor Issues

### Empty `Request error:` on success

The REST diagnostics currently print an empty `Request error:` line even after a successful request. This is cosmetic.

### Same QR can be emitted again after cooldown

With:

```text
Duplicate cooldown = 2 seconds
```

a QR kept in front of the camera long enough can be emitted again after the cooldown.

Future kiosk flow should stop scanning or move to another state after successful validation.

### MediaPipe package reproducibility

The large local `.tgz` installer was not committed to Git. A reproducible package-install strategy should eventually replace reliance on a local tarball.

---

## 21. Current Responsibilities

```text
KioskFaceDetectorRunner
→ face CV + shared camera texture access

KioskCameraBridge
→ connects MediaPipe camera texture to QR scanner

TextureQrCodeScanner
→ QR decoding from external texture

WebcamQrCodeScanner
→ standalone QR demo with owned webcam

QRCodeScannerDemoController
→ QR demo/debug UI

QrTicketValidationDemoController
→ QR event → ticket validation

RestTicketService
→ HTTP client

ASP.NET Core
→ REST API

KioskDbContext / SQLite
→ ticket storage
```

---

## 22. Next Step — Presence Layer

Next component:

```text
KioskPresenceController
```

Target:

```text
Face
OR QR
OR Touch
OR NFC / RFID
       |
       v
 RegisterActivity()
       |
       v
      Active
       |
 no activity for N seconds
       |
       v
      Idle
```

Important distinction:

```text
KioskFaceDetectorRunner
→ sensor-level face state
→ ~2-second loss hysteresis

KioskPresenceController
→ application-level presence/activity
→ longer inactivity timeout, e.g. 10 seconds
```

Face loss must not immediately force Idle because a visitor may cover their face while showing a phone or ticket.

---

## 23. Planned Kiosk Flow

After presence:

```text
Idle
→ WaitingForTicket
→ ValidatingTicket
→ Experience
→ Result / Reset
→ Idle
```

Presence remains a lower-level activity signal and is not the whole kiosk state machine.

---

## 24. Milestone

Completed:

```text
[✓] QR scanning
[✓] ZXing
[✓] QR demo
[✓] REST client
[✓] ASP.NET Core backend
[✓] Bearer test authentication
[✓] timeout / retry tests
[✓] EF Core
[✓] SQLite
[✓] Face Detection
[✓] FaceDetected / FaceLost filtering
[✓] one shared physical webcam
[✓] shared MediaPipe camera texture
[✓] TextureQrCodeScanner
[✓] IQRCodeScanner abstraction
[✓] integrated KioskMain scene
[✓] Face + QR simultaneous operation
[✓] QR → REST → SQLite integrated validation
```

Next:

```text
[ ] KioskPresenceController
[ ] common RegisterActivity() hook
[ ] QR activity integration
[ ] touch activity integration
[ ] NFC/RFID activity integration
[ ] kiosk application state machine
```
