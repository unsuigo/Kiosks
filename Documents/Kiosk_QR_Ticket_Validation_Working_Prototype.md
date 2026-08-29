# Kiosk QR Ticket Validation — Working Prototype

## 1. Purpose

This document describes the current working prototype implemented for the kiosk project.

The prototype validates a ticket scanned from a QR code by sending the decoded ticket code from Unity to a local ASP.NET Core REST API. The backend checks the ticket against a SQLite database and returns a validation result to Unity.

The current implementation is intended as a **working technical prototype / learning implementation**, not as a production-ready backend.

---

## 2. Current End-to-End Flow

```text
Visitor
  ↓
Shows QR code
  ↓
Webcam
  ↓
WebcamQrCodeScanner
  ↓
QR payload: "2660488771"
  ↓
QrCodeDetected event
  ↓
QrTicketValidationDemoController
  ↓
ITicketService
  ↓
RestTicketService
  ↓
JSON serialization
  ↓
HTTP POST
  ↓
Authorization: Bearer <token>
  ↓
ASP.NET Core REST API
  ↓
Entity Framework Core
  ↓
SQLite database
  ↓
Tickets table
  ↓
Validation result
  ↓
HTTP response + JSON
  ↓
Unity deserialization
  ↓
Result shown in Unity Console
```

---

## 3. Unity Project Structure

Current relevant structure:

```text
Assets/
└── Kiosk/
    ├── QRCode/
    │   ├── Runtime/
    │   ├── Demo/
    │   └── Tests/
    │       ├── EditMode/
    │       └── PlayMode/
    │
    └── TicketValidation/
        ├── Runtime/
        │   ├── ITicketService.cs
        │   ├── RestTicketService.cs
        │   ├── TicketValidationRequest.cs
        │   └── TicketValidationResponse.cs
        │
        └── Demo/
            ├── TicketValidationDemoController.cs
            └── QrTicketValidationDemoController.cs
```

The project currently follows a **feature-based structure**.

- `Runtime` — code used by the application at runtime.
- `Demo` — manual test/demo components and scenes.
- `Tests` — automated EditMode / PlayMode tests.

---

## 4. QR Scanner

The QR scanner is already implemented and working.

### Responsibilities

The scanner:

- accesses the webcam through `WebCamTexture`;
- captures camera frames;
- decodes QR codes using ZXing;
- emits the decoded payload as a string;
- suppresses repeated detections using a duplicate cooldown;
- exposes scanner state and diagnostic information.

Example decoded payload:

```text
2660488771
```

The QR scanner does **not** know anything about tickets, REST, databases, or backend logic.

It only produces a decoded string.

This separation is intentional.

---

## 5. Ticket Validation Client

### `ITicketService`

`ITicketService` is the abstraction used by the Unity side.

Conceptually:

```text
Unity logic
    ↓
ITicketService
    ↓
RestTicketService
```

This keeps UI / QR logic independent of the concrete REST implementation.

### `RestTicketService`

`RestTicketService` is responsible for:

- creating the request DTO;
- serializing it to JSON;
- creating a `UnityWebRequest`;
- setting the HTTP method to `POST`;
- setting `Content-Type: application/json`;
- setting the `Authorization` header;
- sending the request;
- receiving the response;
- checking HTTP/network errors;
- deserializing the response JSON;
- invoking success/error callbacks.

Current endpoint:

```text
POST http://localhost:5123/api/tickets/validate
```

---

## 6. Request / Response Format

### Request DTO

Unity sends:

```json
{
  "code": "2660488771"
}
```

The JSON is created from `TicketValidationRequest`.

Serialization:

```text
C# object
  ↓ JsonUtility.ToJson()
JSON string
  ↓ UTF-8
byte[]
  ↓ UploadHandlerRaw
HTTP request body
```

### Response DTO

Example successful response:

```json
{
  "valid": true,
  "message": "Ticket is valid"
}
```

Unity receives the response through `DownloadHandlerBuffer`.

Deserialization:

```text
HTTP response body
  ↓
JSON string
  ↓ JsonUtility.FromJson<T>()
TicketValidationResponse
```

---

## 7. Authentication

The prototype currently uses a simple Bearer token.

Example header:

```http
Authorization: Bearer kiosk-test-token-123
```

Backend behavior:

```text
Missing / invalid token
→ HTTP 401 Unauthorized

Correct token
→ request processing continues
```

### Important

The current token is hardcoded and is suitable **only for the prototype**.

Production credentials must not be hardcoded in source code or committed to Git.

---

## 8. HTTP Status Handling

The prototype currently tests several HTTP scenarios.

### 200 OK

The backend successfully processed the request.

A ticket can still be invalid:

```json
{
  "valid": false,
  "message": "Ticket not found"
}
```

This is a **business result**, not an HTTP failure.

### 400 Bad Request

Used when the client sends invalid data, for example an empty ticket code.

```text
HTTP 400
```

### 401 Unauthorized

Used when authentication fails.

```text
HTTP 401
```

### 500 Internal Server Error

Used in the prototype to simulate an internal backend failure.

Test input:

```text
SERVER_ERROR
```

### HTTP status `0`

Unity uses response code `0` when no valid HTTP response was received.

Examples:

- backend is offline;
- destination host cannot be reached;
- request timeout.

---

## 9. Timeout and Retry

Current Unity request timeout:

```text
3 seconds
```

The prototype includes a special backend test code:

```text
TIMEOUT
```

For this value the backend intentionally waits approximately 10 seconds.

Unity therefore cancels the request after its configured timeout.

Example:

```text
HTTP status: 0
Request result: ConnectionError
Request error: Request timeout
```

### Retry

Current prototype behavior:

```text
Maximum attempts: 2
```

Flow:

```text
Attempt 1
  ↓
Temporary connection error / timeout
  ↓
Wait 1 second
  ↓
Attempt 2
  ↓
Success OR final error
```

A new `UnityWebRequest` instance is created for every attempt.

Retry is currently applied to connection errors.

### Production note

Retries must not be applied blindly to every operation.

A retry is appropriate mainly for **temporary failures** and operations that are safe to repeat.

Operations that change server state may require idempotency protection.

---

## 10. ASP.NET Core Backend

The test backend is located outside the Unity `Assets` folder:

```text
Kiosk_test/
└── BackendMock/
    ├── Program.cs
    ├── BackendMock.csproj
    ├── Models/
    │   └── Ticket.cs
    ├── Data/
    │   └── KioskDbContext.cs
    └── kiosk.db
```

The backend is a separate .NET application.

It must **not** be placed inside Unity `Assets`, because Unity would try to compile backend source files as Unity scripts.

Current runtime:

```text
.NET 8
```

---

## 11. REST API Endpoints

### Health check

```http
GET /health
```

Example response:

```json
{
  "status": "ok"
}
```

Used to verify that the backend is alive.

### Ticket validation

```http
POST /api/tickets/validate
```

Headers:

```http
Content-Type: application/json
Authorization: Bearer kiosk-test-token-123
```

Body:

```json
{
  "code": "2660488771"
}
```

---

## 12. SQLite Database

The prototype uses SQLite through Entity Framework Core.

Database file:

```text
BackendMock/kiosk.db
```

Additional SQLite files such as:

```text
kiosk.db-wal
kiosk.db-shm
```

may exist while the database is running. They are normal SQLite support files.

### Ticket model

Current entity:

```text
Ticket
├── Id
├── Code
└── IsActive
```

Conceptually the database table is:

```text
Tickets
────────────────────────────
Id    Code          IsActive
1     2660488771    true
2     5555555555    true
3     9999999999    false
```

---

## 13. Entity Framework Core

The backend uses Entity Framework Core as an ORM.

The main context is:

```text
KioskDbContext
```

Conceptually:

```text
KioskDbContext
    ↓
DbSet<Ticket>
    ↓
Tickets table
```

The current query is equivalent to:

```csharp
await db.Tickets.FirstOrDefaultAsync(
    t => t.Code == request.Code);
```

EF Core converts this LINQ expression into SQL similar to:

```sql
SELECT "t"."Id", "t"."Code", "t"."IsActive"
FROM "Tickets" AS "t"
WHERE "t"."Code" = @__request_Code_0
LIMIT 1;
```

The ticket code is passed as a SQL parameter instead of being directly concatenated into the SQL string.

---

## 14. Database Initialization

The prototype currently uses:

```text
Database.EnsureCreated()
```

If the database does not exist, EF Core creates it.

Test data is then inserted when the `Tickets` table is empty.

This is convenient for the prototype but is **not the intended production database migration strategy**.

For a real system, database migrations should normally be used.

---

## 15. Current Test Tickets

### Valid active ticket

```text
2660488771
```

Expected:

```text
HTTP 200
valid = true
message = "Ticket is valid"
```

### Second active ticket

```text
5555555555
```

Expected:

```text
HTTP 200
valid = true
message = "Ticket is valid"
```

### Inactive ticket

```text
9999999999
```

Expected:

```text
HTTP 200
valid = false
message = "Ticket is inactive"
```

### Missing ticket

Example:

```text
123456
```

Expected:

```text
HTTP 200
valid = false
message = "Ticket not found"
```

### Simulated backend error

```text
SERVER_ERROR
```

Expected:

```text
HTTP 500
```

### Simulated timeout

```text
TIMEOUT
```

Expected in Unity:

```text
HTTP status: 0
Request result: ConnectionError
Request error: Request timeout
```

---

## 16. QR → REST Integration

`QrTicketValidationDemoController` connects the QR scanner with the ticket service.

Conceptual flow:

```text
WebcamQrCodeScanner
       ↓
QrCodeDetected(string code)
       ↓
QrTicketValidationDemoController
       ↓
ITicketService.ValidateTicket(...)
       ↓
RestTicketService
```

The controller subscribes to:

```text
QrCodeDetected
```

and sends the decoded QR payload to the backend.

A simple `isValidating` flag is used to prevent several ticket validation requests from running at the same time.

The QR scanner also has its own duplicate suppression mechanism.

---

## 17. Verified End-to-End Test

The complete physical-to-database flow has been tested successfully.

Test QR payload:

```text
2660488771
```

Observed Unity result:

```text
QR received: '2660488771'
HTTP attempt 1/2
HTTP status: 200
Request result: Success
TICKET RESULT: valid=True, message='Ticket is valid'
```

This confirms the complete pipeline:

```text
Camera
→ QR decode
→ Unity event
→ REST client
→ Authentication
→ ASP.NET Core
→ Entity Framework Core
→ SQLite
→ Ticket lookup
→ JSON response
→ Unity result
```

---

## 18. Running the Prototype

### Start backend

Open a terminal in:

```text
D:\Development\Unity\Tests\Kiosk_test\BackendMock
```

Run:

```bash
dotnet run
```

Expected:

```text
Now listening on: http://localhost:5123
```

Verify:

```text
http://localhost:5123/health
```

Expected:

```json
{"status":"ok"}
```

### Start Unity

1. Open the QR demo scene.
2. Ensure the QR scanner is configured with the physical webcam.
3. Ensure `QrTicketValidationDemoController` has a reference to the QR scanner.
4. Ensure backend URL is:

```text
http://localhost:5123
```

5. Enter Play Mode.
6. Show a valid QR code to the webcam.

---

## 19. Current Prototype Limitations

The following items are intentionally simplified:

- authentication token is hardcoded;
- backend runs only on `localhost`;
- SQLite is local;
- database is created using `EnsureCreated`;
- test data is seeded directly in code;
- no production configuration management;
- no HTTPS configuration for the local prototype;
- no refresh-token/authentication service;
- no user/device roles;
- no production monitoring;
- no structured application-level logging;
- no offline ticket cache;
- no synchronization strategy;
- no production deployment/update mechanism;
- no admin interface;
- no database migrations;
- no real exhibition/ticket provider integration.

---

## 20. Production-Oriented Next Steps

Possible future improvements:

```text
Configuration / secrets
→ move URLs and credentials out of source code

Authentication
→ real device credentials / JWT / API keys

Database
→ migrations
→ PostgreSQL or production database

Architecture
→ application/service layer
→ repositories only if actually useful
→ validation layer

Reliability
→ structured logs
→ health monitoring
→ offline mode
→ local cache
→ retry/backoff strategy

Security
→ HTTPS
→ secure credential storage
→ token rotation
→ authorization rules

Deployment
→ build artifacts
→ automatic kiosk startup
→ watchdog
→ remote updates
→ rollback

Kiosk flow
→ Idle
→ Scan Ticket
→ Validating
→ Experience
→ Result
→ Reset
```

---

## 21. Key Concepts Demonstrated by the Prototype

The current working version demonstrates practical understanding of:

- REST API;
- HTTP request / response;
- GET / POST;
- endpoints;
- JSON;
- serialization / deserialization;
- HTTP headers;
- Bearer authentication;
- HTTP status codes;
- network errors;
- timeout;
- retry;
- callbacks;
- Unity coroutines;
- `UnityWebRequest`;
- resource disposal with `using`;
- ASP.NET Core Minimal API;
- dependency injection;
- Entity Framework Core;
- ORM;
- LINQ database queries;
- SQLite;
- basic relational database concepts;
- QR scanning;
- event-driven integration;
- separation between client, backend, and database.

---

## 22. Current Status

**Status: Working prototype**

The following complete path is verified:

```text
Physical QR code
→ Webcam
→ Unity QR Scanner
→ Ticket code
→ REST request
→ Authentication
→ ASP.NET Core
→ SQLite lookup
→ Ticket validation
→ REST response
→ Unity
```

The prototype is ready to be kept as a reference implementation and used as the basis for further kiosk/backend development.
