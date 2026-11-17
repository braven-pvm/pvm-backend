# PVM Scanner (Android)

The phone app for document capture (design `docs/superpowers/specs/2026-11-17-document-capture-design.md`,
sections 6.2 and 6.9). It is a capture device only: scan, one PDF per paper document, queue, upload.

- Package name `za.co.pvm.scanner`, app label "PVM Scanner". Android only; side-loaded.
- The phone needs Google Play services (the ML Kit document scanner runs in it) and Android 7.0
  (API 24) or later.

## Scan flow

1. The user opens the app. The app remembers the sign-in and the last document type and opens
   the camera at once.
2. The ML Kit scanner finds the paper edges and captures the page; the user can adjust the
   corners, add pages (up to 20), and taps Done.
3. The app moves the PDF into its upload queue and opens the camera again for the next document.
4. When the user closes the scanner, the batch screen shows the documents of this session,
   newest first, with their state: Waiting, Uploading, Received, or Failed. A large
   "Scan next document" button opens the camera again.

There is no text field in the scan flow. The document type is shown at the top; it is POD by
default and remembered. A chooser appears only when the backend returns more than one type.

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
- One upload at a time, oldest first, to `POST /api/documents` with `file`, `clientDocumentId`
  (a GUID made on the phone), `documentType` and `pageCount`.
- 200 or 201 (the status alone counts): the item is saved as Received, then the local PDF is
  deleted.
- Network error, timeout or 5xx: the item waits and is retried after 5 s, 10 s, 20 s, ... up to
  5 minutes, always with the same `clientDocumentId`, so the backend ignores a second copy. The
  item goes behind the other waiting items, so one bad item does not block the rest. The queue
  also retries when the app comes back to the foreground.
- 400 or 413: the item is Failed with a plain-language reason and keeps its PDF. The user can
  remove a failed item, after a confirmation. The backend accepts PDFs up to 15 MB
  (`DocumentCapture:MaxUploadBytes`, 15728640 bytes); a larger scan answers 413, so scan a long
  delivery note as more than one document.
- 401: the app gets a token silently once more and repeats the upload. A second 401 (or no
  token) holds the queue and shows "Sign in again"; nothing is lost.
- 403: the queue holds and tells the user to ask for the Operator role.

## Build settings (`--dart-define`)

Nothing environment-specific is in the code. Every build passes these four values:

| Value | What | Where it comes from |
| --- | --- | --- |
| `API_BASE_URL` | The PVM API address, for example `https://ca-pvm-api-qa.<env>.southafricanorth.azurecontainerapps.io` | The API container app URL (`docs/runbooks/azure-qa-provisioning-playbook.md` for QA). |
| `ENTRA_TENANT_ID` | The Entra tenant (directory) ID; the same tenant as the workbench | Key Vault secret `auth--tenantid` (backend `Auth:TenantId`, workbench `AUTH_ENTRA_TENANT_ID`). |
| `ENTRA_CLIENT_ID` | The client ID of the **existing API app registration** (the app is a public client of it) | Key Vault secret `auth--api-clientid` (backend `Auth:Audience`). For QA: the `pvm-backend-qa-api` app in `docs/runbooks/workbench-auth-qa.md`. |
| `ENTRA_API_SCOPE` | The API scope the workbench requests, `api://<API client ID>/access_as_user` | Key Vault secret `auth--apiscope` (workbench `AUTH_API_SCOPE`). |

A build without these values starts with a "built without its server settings" screen.

```powershell
cd mobile/scanner
flutter test
flutter analyze
flutter build apk --release `
  --dart-define=API_BASE_URL=https://<api host> `
  --dart-define=ENTRA_TENANT_ID=<tenant id> `
  --dart-define=ENTRA_CLIENT_ID=<API app client id> `
  --dart-define=ENTRA_API_SCOPE=api://<API app client id>/access_as_user
# APK: build/app/outputs/flutter-apk/app-release.apk
```

The first Gradle build takes about 25 minutes. `android/gradle.properties` sets
`kotlin.incremental=false`, because the incremental Kotlin compile fails when the pub cache (C:)
and the project (F:) are on different drives.

## Sign-in

- Package: [`msal_auth`](https://pub.dev/packages/msal_auth) 3.5.x (MSAL for Android 8.x). It
  works with Flutter 3.47 / Dart 3.13. The app uses single account mode, the browser as the
  sign-in user agent, and no broker app (`assets/msal_config.json`:
  `"broker_redirect_uri_registered": false`, `"authorization_user_agent": "BROWSER"`).
- Authority: `https://login.microsoftonline.com/<ENTRA_TENANT_ID>`, given with each interactive
  sign-in; silent renewals use the account's own authority.
- Scope: `ENTRA_API_SCOPE`, the same scope as the workbench. The backend accepts the token
  because its audience is the API client ID (`Auth:Audience`, as the GUID or `api://<GUID>`).
- Access: PVM roles, not Entra app roles. `POST /api/documents` needs the `Documents.Capture`
  policy (Operator or Admin); `GET /api/documents/types` needs a read role (Viewer, Operator or
  Admin). An administrator pre-authorizes the user in the workbench at `/admin/users`.
- The redirect URI is `msauth://za.co.pvm.scanner/<URL-encoded signature hash>`. The app
  computes it at run time from the key that signed the APK and shows it in Settings
  ("Sign-in redirect URI (for IT)") and under a failed sign-in. The manifest's
  `BrowserTabActivity` filter matches any path for host `za.co.pvm.scanner`, so the same build
  setup works with the debug key and the release key.

### Owner action: Entra Android platform entry

An Entra administrator does this once per signing key, for each environment's API app
registration (QA: `pvm-backend-qa-api`; production: its API app):

1. Get the signature hash of the key that signs the APK (Base64 of the SHA-1 of the certificate):

   ```bash
   # Release key (the keystore named in android/key.properties):
   keytool -exportcert -alias <keyAlias> -keystore <storeFile> | openssl sha1 -binary | openssl base64
   # Debug key (used when android/key.properties is missing):
   keytool -exportcert -alias androiddebugkey -keystore ~/.android/debug.keystore -storepass android -keypass android | openssl sha1 -binary | openssl base64
   ```

   Or install the APK and read the redirect URI in the app (Settings, or the text under a failed
   sign-in).
2. Azure portal > App registrations > the API app > Authentication > Add a platform > Android.
   Package name `za.co.pvm.scanner`, signature hash from step 1. Save. The portal shows the
   redirect URI `msauth://za.co.pvm.scanner/<URL-encoded hash>`; it must equal the value the app
   shows.
3. Keep "Allow public client flows" as it is; the Android platform entry is enough for the
   authorization code flow with PKCE that MSAL uses.
4. If the tenant does not allow user consent, grant admin consent for the API's own
   `access_as_user` scope (API permissions > add the API's own scope > Grant admin consent).

A different signing key (for example a test build signed with the debug key) has a different
hash and needs its own entry.

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
in the build). That is fine for a test install only: the debug key differs per machine, so the
redirect URI differs too.

Create a keystore once, from `mobile/scanner`, so that it lands under `android/` (git-ignored),
and keep it, with its passwords, in the company password store (not in Git):

```bash
keytool -genkey -v -keystore android/pvm-scanner-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
```

Never commit a keystore or `key.properties`; `*.jks`, `*.keystore` and `key.properties` are
git-ignored (`mobile/scanner/.gitignore` and `android/.gitignore`). An
update of an installed app must be signed with the same key.

## Code map

| Path | What |
| --- | --- |
| `lib/main.dart`, `lib/app.dart` | Start, settings check, sign-in or batch screen. |
| `lib/config.dart` | The `--dart-define` values. |
| `lib/auth/auth_service.dart` | `AuthService` and the `msal_auth` implementation. |
| `lib/api/documents_api.dart` | API client: upload and document types; upload outcomes. |
| `lib/queue/upload_queue.dart` | The persistent upload queue. |
| `lib/scanner/` | `DocumentScanner` interface and the ML Kit implementation. A different edge detector for some paper shapes goes behind the same interface. |
| `lib/screens/` | Sign-in, batch and settings screens. |
| `android/app/src/main/kotlin/.../MainActivity.kt` | Computes the MSAL redirect URI from the APK signature. |
| `test/` | Queue and API client tests (fakes; no device needed). |
