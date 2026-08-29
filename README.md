# Kiosk Test

Reusable Unity foundation for interactive exhibition kiosks and installations.

The project explores a production-oriented kiosk architecture built around Unity, computer vision, QR ticket scanning, backend validation, local data storage, visitor presence detection, and hardware integration.

The current prototype already implements a working end-to-end flow from a physical camera to ticket validation through a REST backend.

---

## Unity version

- Unity 6.3
- `6000.3.19f1`

---

## Current status

Working integrated kiosk prototype.

Implemented and verified:

- Single shared webcam for multiple computer-vision tasks
- MediaPipe Face Detection
- QR code recognition with ZXing
- Shared camera texture between MediaPipe and QR scanner
- REST ticket validation
- ASP.NET Core backend
- SQLite database
- Entity Framework Core
- Test Bearer authentication
- Request timeout and retry handling
- Visitor presence detection
- Face-based activity tracking
- QR-based activity tracking
- Idle / Active presence state
- Modular interfaces for scanner and detector implementations
- Separate runtime, demo and integration responsibilities

---

## Current architecture

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
                  |                         |
                  |            +------------+------------+
                  |            |                         |
                  v            v                         v
           Presence       Presence Activity      Ticket Validation
           Activity                                    |
                  \                                     v
                   \                              REST Client
                    \                                    |
                     +------> Kiosk Presence             v
                              Controller          ASP.NET Core API
                                  |                      |
                           Idle / Active                 v
                                                   SQLite / EF Core

                                                   Features
Face Detection

Face detection is implemented using the MediaPipe Unity Plugin and the BlazeFace short-range model.

Current configuration:

Delegate:                 CPU
Image Read Mode:          CPUAsync
Model:                    BlazeFace short-range
Running Mode:             LIVE_STREAM
Min Detection Confidence: 0.5
Min Suppression Threshold:0.3
Num Faces:                3

The kiosk-specific runner is:

KioskFaceDetectorRunner

It exposes:

HasFace
FaceDetected
FaceLost
CurrentCameraTexture

A short face-loss delay is used to prevent individual missed frames from constantly switching the face state.

Current behavior:

Face appears
→ FaceDetected immediately

Temporary missed detections
→ ignored

Face absent for ~2 seconds
→ FaceLost
Shared Camera Architecture

The original QR scanner and MediaPipe sample both used their own webcam access.

The integrated kiosk scene now uses:

MediaPipe ImageSource
        |
        v
Shared Texture
        |
        +--> Face Detection
        |
        +--> KioskCameraBridge
                  |
                  v
          TextureQrCodeScanner

KioskCameraBridge transfers the currently active MediaPipe texture to the QR scanner.

Verified camera resolution:

1280 × 720
QR Code Scanning

QR recognition uses ZXing.

There are currently two scanner implementations.

WebcamQrCodeScanner

Standalone scanner used by the original QR demo.

It owns its own:

WebCamTexture

and is useful for isolated QR testing.

TextureQrCodeScanner

Used by the integrated kiosk scene.

It does not open or stop the webcam.

Instead it receives an external Unity Texture supplied by KioskCameraBridge.

Responsibilities:

Read camera frames
Decode QR codes
Duplicate suppression
Scanner state
Diagnostics
Optional camera preview
QR detection events

Both scanner implementations use the common interface:

IQRCodeScanner

This keeps the rest of the application independent from how the camera source is implemented.

QR Scanner UI

The QR demo UI is handled separately by:

QRCodeScannerDemoController

It is responsible only for:

Start Scan button
Stop Scan button
Scanner status
Last decoded QR value

The scanner itself remains a separate runtime component.

This separation allows the same UI controller to work with either:

WebcamQrCodeScanner

or:

TextureQrCodeScanner
Ticket Validation

Detected QR codes are sent to the local backend through:

QrTicketValidationDemoController
        |
        v
ITicketService
        |
        v
RestTicketService

Current endpoint:

POST http://localhost:5123/api/tickets/validate

Example request:

{
  "code": "2660488771"
}

Example successful response:

{
  "valid": true,
  "message": "Ticket is valid"
}
REST client

The Unity REST client currently supports:

POST requests
JSON serialization
JSON response parsing
HTTP status handling
Connection-error handling
Timeout handling
Limited retry
Bearer authentication

Current prototype settings:

Timeout:       3 seconds
Max attempts:  2
Retry delay:   1 second

Current test authorization:

Authorization: Bearer kiosk-test-token-123

The hardcoded token is for development only.

Backend

A local ASP.NET Core backend is included for development and integration testing.

Technology stack:

.NET 8
ASP.NET Core Minimal API
Entity Framework Core
SQLite

Backend project:

BackendMock

Database:

kiosk.db
Starting the backend

From Git Bash:

cd /d/Development/Unity/Tests/Kiosk_test/BackendMock
dotnet run

The API should start at:

http://localhost:5123

Health check:

http://localhost:5123/health

Expected response:

{
  "status": "ok"
}
Test tickets

Current database test values:

Ticket code	Result
2660488771	Valid
5555555555	Valid
9999999999	Inactive
Unknown code	Not found
End-to-end ticket flow

The following flow has been verified:

Physical QR code
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
HTTP 200 + JSON
      |
      v
Unity

Example result:

QR received: '2660488771'
HTTP attempt 1/2
HTTP status: 200
Request result: Success
TICKET RESULT: valid=True, message='Ticket is valid'
Visitor Presence

The project contains a kiosk-level presence system:

KioskPresenceController

Presence is intentionally separated from raw face detection.

The face detector answers:

Is a face currently detected?

The presence controller answers:

Is a visitor currently interacting with the kiosk?

Current activity sources:

Face Detection
QR Detection

Both feed:

RegisterActivity()

Current presence states:

Idle
Active
Presence behavior

Current configuration:

Face loss filtering:   ~2 seconds
Presence timeout:      10 seconds

Example:

Face detected
→ ACTIVE

Face disappears
→ Face detector waits ~2 seconds

FaceLost
→ kiosk remains ACTIVE

No further activity for 10 seconds
→ IDLE

QR detection also refreshes presence activity.

This prevents the kiosk from becoming idle when a visitor temporarily covers their face with a phone or ticket.

Main scene

The integrated prototype scene is:

KioskMain

Conceptual hierarchy:

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
Project organization

The kiosk-specific code is organized by feature.

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

Main namespaces:

Kiosk.Camera
Kiosk.ComputerVision.FaceDetection
Kiosk.QR
Kiosk.QR.Demo
Kiosk.TicketValidation
Kiosk.Presence

Third-party MediaPipe code remains under its own:

Mediapipe.*

namespace hierarchy.

Design principles

The current prototype follows several architectural rules.

One hardware owner

A physical device should normally have a single owner.

For the webcam:

MediaPipe owns the webcam.

Other components consume its data.

Interfaces between systems

Subsystems communicate through abstractions where useful:

IFaceDetector
IQRCodeScanner
ITicketService

This allows implementations to be replaced without rewriting higher-level kiosk logic.

Separate hardware, application and UI responsibilities

For example:

TextureQrCodeScanner
→ QR recognition

QRCodeScannerDemoController
→ UI

QrTicketValidationDemoController
→ business/integration flow

KioskPresenceController
→ visitor activity state
Current milestone

Completed:

[✓] Unity kiosk foundation
[✓] MediaPipe integration
[✓] Face detection
[✓] Face detection hysteresis
[✓] ZXing QR scanning
[✓] QR duplicate filtering
[✓] Shared webcam architecture
[✓] Shared camera texture
[✓] KioskCameraBridge
[✓] IQRCodeScanner abstraction
[✓] REST client
[✓] ASP.NET Core backend
[✓] Bearer test authentication
[✓] Timeout / retry behavior
[✓] Entity Framework Core
[✓] SQLite ticket storage
[✓] QR → REST → DB validation
[✓] KioskPresenceController
[✓] Face activity
[✓] QR activity
[✓] Idle / Active presence state
[✓] Integrated KioskMain scene
Next steps

Planned development:

[ ] Touch / pointer activity
[ ] NFC / RFID input
[ ] Full kiosk state machine
[ ] WaitingForTicket state
[ ] Validating state
[ ] Experience state
[ ] Reset flow
[ ] Stop repeated ticket validation after successful scan
[ ] Offline ticket validation strategy
[ ] Diagnostics and persistent logging
[ ] Hardware disconnect / recovery handling
[ ] Watchdog / auto-restart
[ ] Automatic startup on Windows kiosk PC
[ ] Remote status / monitoring
[ ] Deployment and update strategy

Target application flow:

Idle
  ↓
Visitor Detected
  ↓
Waiting For Ticket
  ↓
Validating Ticket
  ↓
Experience
  ↓
Result / Reset
  ↓
Idle
Purpose

This repository is intended to evolve into a reusable technical foundation for interactive exhibition installations rather than a single-purpose kiosk application.

Future installations should be able to reuse the same core architecture while replacing individual modules such as:

ticket providers;
cameras;
QR scanners;
NFC / RFID devices;
sensors;
backend endpoints;
exhibition experiences;
UI and branding.