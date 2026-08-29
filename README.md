# Interactive Exhibition Kiosk Prototype

A Unity-based prototype for interactive exhibition kiosks combining **computer vision, QR ticket scanning, backend validation, local persistence, and visitor presence tracking**.

The project is designed as a reusable technical foundation rather than a single-purpose kiosk application. The current version demonstrates a working end-to-end flow from a physical camera input to ticket validation through a REST backend.

## Tech stack

- **Unity 6.3** (`6000.3.19f1`)
- **C#**
- **MediaPipe Unity Plugin** — face detection / camera source
- **ZXing** — QR decoding
- **ASP.NET Core (.NET 8)** — local backend
- **Entity Framework Core**
- **SQLite**

## Current status

The integrated `KioskMain` scene currently supports:

- one shared physical webcam;
- MediaPipe face detection;
- QR recognition from the same camera stream;
- REST ticket validation;
- SQLite-backed ticket lookup;
- timeout / retry handling;
- mock Bearer authentication;
- visitor presence tracking;
- face- and QR-driven activity;
- `Idle` / `Active` presence state;
- modular interfaces between camera, scanner, presence, and backend layers.

## Architecture

```text
                         Physical Webcam
                               |
                               v
                      MediaPipe ImageSource
                               |
                      Shared Camera Texture
                        /                 \
                       /                   \
                      v                     v
             Face Detection          TextureQrCodeScanner
                  |                         |
                  |                         v
                  |                    ZXing Decoder
                  |                         |
                  |                    QR Detected
                  |                    /          \
                  v                   v            v
          Presence Activity    Presence Activity   Ticket Validation
                  \                   /                 |
                   \                 /                  v
                    +----> KioskPresenceController   RestTicketService
                              |                          |
                         Idle / Active                   v
                                                 ASP.NET Core API
                                                        |
                                                        v
                                                   EF Core / SQLite
```

### Key design decision: one camera owner

A physical webcam is opened only once.

MediaPipe owns the webcam and exposes the current camera texture. Other systems consume that texture instead of opening the device independently.

```text
MediaPipe ImageSource
        |
        v
Shared Texture
    |         |
    v         v
Face CV   QR Scanner
```

This avoids camera contention and makes it possible to run face detection and QR recognition simultaneously.

## Main components

### `KioskFaceDetectorRunner`

Kiosk-specific wrapper around the MediaPipe Face Detection pipeline.

Responsibilities:

- face detection;
- MediaPipe annotation output;
- `HasFace` state;
- `FaceDetected` / `FaceLost` events;
- access to the current camera texture.

The detector uses a short loss delay so isolated missed frames do not immediately produce a `FaceLost` event.

```text
Face appears
→ FaceDetected immediately

Temporary missed frames
→ ignored

Face continuously absent for ~2 seconds
→ FaceLost
```

### `KioskCameraBridge`

Connects the MediaPipe-owned camera stream to the QR scanner.

```text
KioskFaceDetectorRunner.CurrentCameraTexture
                    |
                    v
            KioskCameraBridge
                    |
                    v
           TextureQrCodeScanner
```

The shared camera texture has been verified at `1280 × 720`.

### `IQRCodeScanner`

Common abstraction for QR scanning.

The project currently contains two implementations:

- `WebcamQrCodeScanner` — standalone scanner that owns its webcam, retained for isolated QR testing;
- `TextureQrCodeScanner` — integrated scanner that consumes an externally supplied texture.

This allows higher-level systems to depend on scanner behavior rather than on a specific camera implementation.

### `TextureQrCodeScanner`

Responsibilities:

- external texture input;
- frame sampling;
- ZXing decoding;
- duplicate suppression;
- scanner state;
- diagnostics;
- optional preview;
- QR detection events.

Current test settings:

```text
Scan rate:              7 scans/sec
Duplicate cooldown:     2 sec
Initialization timeout: 8 sec
Auto start:             disabled
```

### `QRCodeScannerDemoController`

UI-only controller for:

- Start Scan;
- Stop Scan;
- status text;
- last decoded QR value.

The UI is intentionally separated from the scanner implementation.

### `QrTicketValidationDemoController`

Subscribes to `IQRCodeScanner.QrCodeDetected` and starts ticket validation.

```text
QrCodeDetected
      |
      v
QrTicketValidationDemoController
      |
      v
ITicketService
      |
      v
RestTicketService
```

An `isValidating` guard prevents overlapping validation requests.

### `RestTicketService`

Unity REST client responsible for ticket validation.

Current behavior:

- JSON serialization / deserialization;
- POST requests;
- HTTP status handling;
- connection-error handling;
- request timeout;
- limited retry;
- mock Bearer authentication.

Prototype retry policy:

```text
Timeout:       3 sec
Max attempts:  2
Retry delay:   1 sec
```

A fresh `UnityWebRequest` is created for every retry attempt.

### `KioskPresenceController`

Application-level visitor presence logic.

This is intentionally separated from raw face detection.

The detector answers:

> Is a face currently detected?

The presence controller answers:

> Is a visitor currently interacting with the kiosk?

Current activity sources:

```text
Face Detection
QR Detection
      |
      v
RegisterActivity()
      |
      v
Idle <-> Active
```

Current presence timeout: `10 sec`.

This means temporary face loss does not immediately return the kiosk to idle. For example, a visitor can hold a phone or printed ticket in front of their face while still being treated as active.

## End-to-end ticket flow

The following pipeline has been verified:

```text
Physical QR
    |
    v
Webcam
    |
    v
MediaPipe ImageSource
    |
    v
Shared Camera Texture
    |
    v
TextureQrCodeScanner
    |
    v
ZXing
    |
    v
QrCodeDetected
    |
    v
Ticket Validation Controller
    |
    v
RestTicketService
    |
    v
ASP.NET Core
    |
    v
Entity Framework Core
    |
    v
SQLite
    |
    v
HTTP response
    |
    v
Unity
```

Example successful result:

```text
QR received: '2660488771'
HTTP attempt 1/2
HTTP status: 200
Request result: Success
TICKET RESULT: valid=True, message='Ticket is valid'
```

## Backend

The repository includes a small local backend for development and integration testing.

Stack:

```text
.NET 8
ASP.NET Core Minimal API
Entity Framework Core
SQLite
```

Main endpoint:

```http
POST /api/tickets/validate
```

Example request:

```json
{
  "code": "2660488771"
}
```

Example response:

```json
{
  "valid": true,
  "message": "Ticket is valid"
}
```

### Running the backend

From the repository root:

```bash
cd BackendMock
dotnet run
```

Default development address:

```text
http://localhost:5123
```

Health check:

```text
GET /health
```

Expected response:

```json
{
  "status": "ok"
}
```

## Scene structure

Integrated prototype scene:

```text
KioskMain
├── Main Camera
├── Directional Light
├── Main Canvas
├── EventSystem
├── Solution
│   └── KioskFaceDetectorRunner
├── QR Scanner
│   └── TextureQrCodeScanner
├── Canvas
│   └── QRCodeScannerDemoController
├── Ticket Validation Demo
│   └── QrTicketValidationDemoController
├── KioskCameraBridge
└── KioskPresenceController
```

## Project organization

Kiosk-specific code is grouped by feature:

```text
Assets/Kiosk/

Camera/
└── Runtime/

ComputerVision/
└── FaceDetection/
    └── Runtime/

QRCode/
├── Runtime/
├── Demo/
└── Tests/

TicketValidation/
├── Runtime/
└── Demo/

Presence/
└── Runtime/
```

Main namespaces:

```text
Kiosk.Camera
Kiosk.ComputerVision.FaceDetection
Kiosk.QR
Kiosk.QR.Demo
Kiosk.TicketValidation
Kiosk.Presence
```

Third-party MediaPipe code remains under its own `Mediapipe.*` namespace hierarchy.

## Engineering principles demonstrated

### Separation of responsibilities

```text
KioskFaceDetectorRunner
→ computer vision / face state

KioskCameraBridge
→ shared camera transport

TextureQrCodeScanner
→ QR recognition

QRCodeScannerDemoController
→ UI

QrTicketValidationDemoController
→ application integration flow

RestTicketService
→ HTTP communication

KioskPresenceController
→ visitor activity state
```

### Interface-driven integration

Subsystems communicate through abstractions where useful:

```text
IFaceDetector
IQRCodeScanner
ITicketService
```

This keeps higher-level kiosk logic independent from individual implementations.

### Sensor state vs. application state

Raw sensor output is not treated as application state directly.

```text
Face detector
→ short CV hysteresis

Presence controller
→ longer visitor inactivity timeout
```

This reduces flicker and avoids unrealistic kiosk behavior when sensor data is temporarily lost.

## Current milestone

Completed:

- [x] Unity kiosk foundation
- [x] MediaPipe integration
- [x] Face detection
- [x] Face detection hysteresis
- [x] ZXing QR scanning
- [x] QR duplicate filtering
- [x] Shared webcam architecture
- [x] Shared camera texture
- [x] `KioskCameraBridge`
- [x] `IQRCodeScanner` abstraction
- [x] REST client
- [x] ASP.NET Core backend
- [x] Mock authentication
- [x] Timeout / retry behavior
- [x] Entity Framework Core
- [x] SQLite ticket storage
- [x] QR → REST → DB validation
- [x] `KioskPresenceController`
- [x] Face activity
- [x] QR activity
- [x] `Idle` / `Active` presence state
- [x] Integrated `KioskMain` scene

## Known limitations

This is still a prototype. Current limitations include:

- the same QR may be emitted again after the duplicate cooldown;
- a successfully validated ticket does not yet transition the application into a dedicated experience state;
- authentication is development-only;
- offline validation is not implemented yet;
- kiosk watchdog / auto-recovery is not implemented yet;
- hardware disconnect recovery is still limited;
- deployment and remote monitoring are not implemented yet.

## Next steps

Planned:

- [ ] Touch / pointer activity
- [ ] NFC / RFID input
- [ ] Full kiosk state machine
- [ ] `WaitingForTicket`
- [ ] `Validating`
- [ ] `Experience`
- [ ] Result / reset flow
- [ ] Stop repeated validation after a successful scan
- [ ] Offline ticket validation strategy
- [ ] Persistent diagnostics / logging
- [ ] Hardware disconnect and recovery handling
- [ ] Watchdog / automatic restart
- [ ] Windows kiosk auto-start
- [ ] Remote status / monitoring
- [ ] Deployment and update strategy

Target application flow:

```text
Idle
  |
  v
Visitor Detected
  |
  v
Waiting For Ticket
  |
  v
Validating
  |
  v
Experience
  |
  v
Result / Reset
  |
  v
Idle
```

## Goal

The long-term goal is a reusable kiosk framework for interactive exhibitions where individual modules can be replaced without rewriting the whole application.

Potential replaceable modules include:

- ticket providers;
- QR implementations;
- cameras;
- NFC / RFID readers;
- sensors;
- backend endpoints;
- exhibition experiences;
- UI / branding.
