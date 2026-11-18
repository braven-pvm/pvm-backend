# PVM Scanner (Android)

The phone app for document capture (design `docs/superpowers/specs/2026-11-17-document-capture-design.md`,
sections 6.2, 6.9 and 6.10). It is a capture device only: scan, one PDF per paper document, queue, upload.

- Package name `za.co.pvm.scanner`, app label "PVM Scanner". Android only; side-loaded.
- The phone needs Google Play services (the ML Kit document scanner runs in it) and Android 7.0
  (API 24) or later.
- No user sign-in: the **device** signs in with a device key it gets by scanning a setup QR code
  (design 6.10). One APK serves QA and production; the server address comes from the QR code.

## Setup (enrolment)

1. An Admin opens **Documents > Scanner devices** in the workbench, selects **Add device**, and
   types a name, for example "Dispatch phone 1". The page shows a setup QR code (one use, 15
   minutes).
2. On the first start (or after the phone was removed or reset) the app shows one button,
   **Scan setup code**. The camera reads the QR code.
3. The QR content must be `{"v":1,"api":"<https URL>","code":"<code>"}`: version 1, an `https`
   API address without user info, query or fragment, and a code. Anything else shows "This is
   not a PVM Scanner setup code."
4. The app calls `POST {api}/api/device/enrol` with `{"code": "..."}`:
   - 200 `{deviceKey, deviceName}`: the key, the API address and the device name go into
     Android encrypted storage (`flutter_secure_storage`), and the scan screen opens.
   - 400: "This setup code is not valid. Ask an admin for a new one."
   - 429: "Too many tries. Wait one minute."
   - No network or a timeout: "No connection. Check the Wi-Fi and try again."
5. At each start the app reads `GET /api/device/config` (device name, document types, active
   staff names) and keeps the last answer in shared preferences for a start without network.

Every device call sends `Authorization: Device <key>` and never follows a redirect (a redirect
would carry the key, or the setup code, to another address; a 3xx counts as a failed call). A
**401** from any device call means the device was revoked or set up again elsewhere: the app
holds the upload loop, deletes the stored key, and shows the setup screen with "This phone was
removed. Ask an admin for a new setup code." The queue file and the PDFs stay; after the next
enrolment they upload with the same client document IDs.

The held queue stays open until the next enrolment opens it again (or the app ends). A scan that
was in the camera when the key went is still saved into it, with the type and staff name it was
taken under; the camera then does not open again.

**Settings** shows the device name, the server address and the app version, and has **Reset this
phone** ("This phone stops scanning until an admin gives a new setup code. Scans not yet sent
stay on the phone."), which deletes the key only and holds the queue in the same way. Scans not yet sent go to the server of the
next setup code.

## Scan flow

1. The user opens the app. If the config has staff names, the app asks **Who is scanning?** with
   the names as large buttons, unless the remembered name is still on the list. The choice is
   remembered across restarts; **Change** on the scan screen picks another name. With no staff
   names the question is skipped.
2. The camera opens. The ML Kit scanner finds the paper edges and captures the page; the user
   can adjust the corners, add pages (up to 20), and taps Done.
3. The app moves the PDF into its upload queue, with the staff ID chosen at that moment, and
   opens the camera again for the next document.
4. When the user closes the scanner, the batch screen shows the documents of this session,
   newest first, with their state: Waiting, Uploading, Received, or Failed. A large
   "Scan next document" button opens the camera again.

There is no text field in the scan flow. The document type is shown at the top; it is POD by
default and remembered. A chooser appears only when the config has more than one type.

## Upload queue

- Files: `<app documents>/upload_queue/queue.json` (the item list; the previous good copy in
  `queue.json.bak`) and `<app documents>/upload_queue/pdfs/<clientDocumentId>.pdf`. The queue
  survives an app restart. Saves run one at a time (temporary file, then rename).
- A new scan: the PDF is copied into the queue folder, the item is saved, and only then is the
  scanner's original deleted.
- Recovery at start: an unreadable `queue.json` is replaced by `queue.json.bak`; without a good
  backup, every PDF in `pdfs/` that no item references becomes a waiting item (type POD, page
  count read from the PDF, its GUID file name kept as the client document ID). A waiting item
  whose PDF is gone after an upload attempt shows as Received, "Sent earlier". If the start
  still fails, the app shows "The app could not start" with "Try again".
- One upload at a time, oldest first, to `POST /api/device/documents` with `file`,
  `clientDocumentId` (a GUID made on the phone), `documentType`, `pageCount`, and `staffId` when
  a name was chosen for that scan.
- 200 or 201 (the status alone counts): the item is saved as Received, then the local PDF is
  deleted.
- Network error, timeout, 403 or 5xx: the item waits and is retried after 5 s, 10 s, 20 s, ...
  up to 5 minutes, always with the same `clientDocumentId`, so the backend ignores a second copy.
  The item goes behind the other waiting items, so one bad item does not block the rest. The
  queue also retries when the app comes back to the foreground.
- 400 or 413: the item is Failed with a plain-language reason and keeps its PDF. The user can
  remove a failed item, after a confirmation. The backend accepts PDFs up to 15 MB
  (`DocumentCapture:MaxUploadBytes`, 15728640 bytes); a larger scan answers 413, so scan a long
  delivery note as more than one document.
- A 400 about `staffId` (the name was turned off while the scan waited) is sent once more
  without the name, so the document is not lost.
- 401: see Setup above. The item stays Waiting with its attempt count unchanged.

## Build

Nothing environment-specific is in the app, and the build takes no `--dart-define` values:

```powershell
cd mobile/scanner
flutter test
flutter analyze
flutter build apk --release
# APK: build/app/outputs/flutter-apk/app-release.apk
```

The first Gradle build takes about 25 minutes. `android/gradle.properties` sets
`kotlin.incremental=false`, because the incremental Kotlin compile fails when the pub cache (C:)
and the project (F:) are on different drives.

The backend must know its own public address (`DocumentCapture:DeviceApiBaseUrl`, infrastructure
parameter `deviceApiBaseUrl`) before the workbench can make setup codes. QA sets it; production
does not yet (`infra/azure/main.parameters.prod.json`).

## Packages

| Package | Version | Use |
| --- | --- | --- |
| `google_mlkit_document_scanner` | 0.7.x | The document scanner (Google Play services). |
| `mobile_scanner` | 7.5.x | The camera for the setup QR code. Uses the bundled ML Kit barcode model on Android (about 3 to 10 MB more APK, works without a download). |
| `flutter_secure_storage` | 11.2.x | The device key, API address and device name in Android encrypted storage (AES-GCM, key wrapped by the Android Keystore). |
| `shared_preferences` | 2.5.x | Document type, staff choice, last device config. Nothing secret. |
| `http` | 1.6.x | The API client. |

## Release signing

`android/app/build.gradle.kts` reads `android/key.properties` when it exists:

```properties
storePassword=<store password>
keyPassword=<key password>
keyAlias=upload
storeFile=../pvm-scanner-upload.jks
# storeFile is relative to android/app; ../pvm-scanner-upload.jks is android/pvm-scanner-upload.jks
```

Without `android/key.properties` the release APK is signed with the debug key (a warning shows
in the build). That is fine for a test install only: the debug key differs per machine, and
Android installs an update only when it is signed with the same key as the installed app.

Create a keystore once, from `mobile/scanner`, so that it lands under `android/` (git-ignored),
and keep it, with its passwords, in the company password store (not in Git):

```bash
keytool -genkey -v -keystore android/pvm-scanner-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
```

Never commit a keystore or `key.properties`; `*.jks`, `*.keystore` and `key.properties` are
git-ignored (`mobile/scanner/.gitignore` and `android/.gitignore`).

## Code map

| Path | What |
| --- | --- |
| `lib/main.dart`, `lib/app.dart` | Start; setup screen or batch screen by the session phase. |
| `lib/device/setup_code.dart` | Parses and checks the setup QR content. |
| `lib/device/device_store.dart` | The device key, API address and name in encrypted storage. |
| `lib/device/device_session.dart` | Enrolment, the queue with the device key, config, revoke (401) and reset. |
| `lib/device/staff.dart` | Whether to ask "Who is scanning?". |
| `lib/api/device_api.dart` | Enrol client and device API client (config, upload); outcomes. |
| `lib/queue/upload_queue.dart` | The persistent upload queue. |
| `lib/scanner/` | `DocumentScanner` interface and the ML Kit implementation. A different edge detector for some paper shapes goes behind the same interface. |
| `lib/screens/` | Setup, QR camera, staff picker, batch and settings screens. |
| `test/` | Setup code, API client, session, staff, queue and screen tests (fakes; no device needed). |
