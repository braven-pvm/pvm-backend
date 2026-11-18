# Document capture: rollout runbook

This runbook takes PR #75 (POD document capture) from an open PR to a QA shadow run, then to
production. Do the parts in order. Each step names the exact screen, command, or value.

Values used below:

| Item | QA | Production |
|---|---|---|
| Key Vault | `kv-pvm-intg-qa` | `kv-pvm-intg-prod` |
| Resource group | `rg-pvm-integrations-qa` | `rg-pvm-integrations-prod` |
| API container app | `ca-pvm-api-qa` | `ca-pvm-api-prod` |
| Worker container app | `ca-pvm-worker-qa` | `ca-pvm-worker-prod` |
| API address | `https://ca-pvm-api-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io` | read it in step F1 |
| Acumatica | `https://devtest1.aboutitgroup.co.za/PVMGroup25R101D250625` | `https://pvm.aboutitgroup.co.za` |
| Entra API app registration | `pvm-backend-qa-api` | the production API app (client ID in `auth--api-clientid` of `kv-pvm-intg-prod`) |

## Part A. Anthropic API key (owner)

Do this before the merge. Without the key, the deploy succeeds, but every scan fails extraction.

1. Open `https://console.anthropic.com` and sign in with the PVM organisation account.
2. Go to **Settings > API Keys**, then select **Create Key**.
3. Name the key `pvm-document-capture-qa`, then select **Add**.
4. Copy the key. The console shows it only once.
5. In the Azure portal, open **Key vaults > kv-pvm-intg-qa > Objects > Secrets**.
6. Select **Generate/Import**.
7. Type the name `anthropic--apikey`, paste the key into **Secret value**, then select **Create**.
8. Do steps 2 to 7 again for production: key name `pvm-document-capture-prod`, vault
   `kv-pvm-intg-prod`.
9. Check both secrets in a terminal that is signed in to Azure:

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-qa --name anthropic--apikey --query attributes.enabled -o tsv
   ```

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-prod --name anthropic--apikey --query attributes.enabled -o tsv
   ```

   Each command must print `true`.

## Part B. Acumatica rights for the integration user (owner)

The integration user is the user name in the Key Vault secret `acumatica--username`. Do these
steps in QA (`devtest1…/PVMGroup25R101D250625`), then in production (`pvm.aboutitgroup.co.za`).

1. Get the user name:

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-qa --name acumatica--username --query value -o tsv
   ```

2. Sign in to Acumatica as an administrator.
3. Open the **Users** screen (SM201010) and open that user. Note the role names on the
   **Roles** tab.
4. Open **Access Rights by Role** (SM201025) and select the integration role from step 3.
5. Set these rights. The backend uses each screen for the reason given:

   | Screen ID | Screen | Right | Used for |
   |---|---|---|---|
   | SO303000 | Invoices | **Edit** | Read invoices, and attach the POD PDF to the invoice |
   | SO301000 | Sales Orders | **View Only** | Find the order from an order number or a client PO number |
   | SO302000 | Shipments | **View Only** | Read the invoice number on the order's shipments |
   | AR303000 | Customers | **View Only** | Read the customer name and the parent customer |

6. Select **Save**.
7. If the role is shared with other users, check that the new rights are acceptable for them.
   If they are not, make a new role `PVM-Integration-Documents` with only the rights in step 5,
   and add it to the integration user on the **Users** screen.

If the screen IDs in your Acumatica menu differ, search for the screen by its name. A missing
right shows later as a `403` in the worker log, and that message names the screen ID.

## Part C. Make the PR ready to merge (Claude)

Main moved after the PR opened, and the PR now has a merge conflict in two workbench files
(`frontend/workbench/src/navigation.ts`, `frontend/workbench/src/ui/app-shell.tsx`). The three
new database migrations must also come after the two migrations that main added
(`AddSellInWorkbookRules`, `AddSellOutImport`). In the same change, Claude adds the Acumatica
link setting to `infra/azure/main.parameters.qa.json` and `main.parameters.prod.json`.

Claude does this when you approve it. The steps are:

1. Merge `origin/main` into the branch and resolve the two conflicts.
2. Make the three document migrations again on top of main's migrations.
3. Add `workbenchAcumaticaBaseUrl` to both parameter files with the Acumatica values in the
   table above.
4. Run the backend, workbench and app tests again, then push.

## Part D. Merge and deploy to QA (owner)

Do Parts A, B and C first.

1. Open https://github.com/braven-pvm/pvm-backend/pull/75.
2. Check that GitHub shows no conflicts.
3. Select **Merge pull request**, then **Confirm merge**.
4. The merge starts the workflow **Deploy QA** automatically. Watch it:

   ```bash
   gh run list --repo braven-pvm/pvm-backend --workflow deploy-qa.yml --limit 1
   ```

5. If the run waits for approval, open it on GitHub and select **Review deployments > qa >
   Approve and deploy**.
6. When the run has finished, check that the worker reads the new queue:

   ```bash
   az containerapp logs show --resource-group rg-pvm-integrations-qa --name ca-pvm-worker-qa --tail 100
   ```

   The log must show no `Anthropic` key error at start.

## Part E. Give the scanner staff access (owner)

1. Open the QA workbench and go to `/admin/users`.
2. For each person who scans, add the person's work email address with the role **Operator**.
   Operators can upload scans and fix documents on the review list. Use **Viewer** for people
   who only look up PODs.

## Part F. Signing key, Entra entry, and the app build (owner, with Claude for the build)

Use one release key for all builds that go to the office phones. The Entra entry depends on it.

1. Make the release key on the computer that builds the app. Keep the passwords in your password
   manager:

   ```bash
   cd mobile/scanner/android
   ```

   ```bash
   keytool -genkey -v -keystore pvm-scanner-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias pvm-scanner
   ```

2. Make the file `mobile/scanner/android/key.properties` with these four lines. Git ignores this
   file and the keystore. Do not commit them:

   ```properties
   storePassword=<the store password from step 1>
   keyPassword=<the key password from step 1>
   keyAlias=pvm-scanner
   storeFile=../pvm-scanner-upload.jks
   ```

3. Get the signature hash of the key:

   ```bash
   keytool -exportcert -alias pvm-scanner -keystore pvm-scanner-upload.jks | openssl sha1 -binary | openssl base64
   ```

4. In the Azure portal, open **Microsoft Entra ID > App registrations > pvm-backend-qa-api >
   Authentication**.
5. Select **Add a platform > Android**.
6. Type the package name `za.co.pvm.scanner` and the signature hash from step 3. Select
   **Configure**, then **Save**.
7. If the tenant does not allow user consent, open **API permissions** on the same app, add the
   app's own `access_as_user` scope, and select **Grant admin consent**.
8. Read the three sign-in values for the build:

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-qa --name auth--tenantid --query value -o tsv
   ```

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-qa --name auth--api-clientid --query value -o tsv
   ```

   ```bash
   az keyvault secret show --vault-name kv-pvm-intg-qa --name auth--apiscope --query value -o tsv
   ```

9. Build the QA app (PowerShell, from the repository root):

   ```powershell
   cd mobile/scanner
   flutter build apk --release `
     --dart-define=API_BASE_URL=https://ca-pvm-api-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io `
     --dart-define=ENTRA_TENANT_ID=<value 1 from step 8> `
     --dart-define=ENTRA_CLIENT_ID=<value 2 from step 8> `
     --dart-define=ENTRA_API_SCOPE=<value 3 from step 8>
   ```

   The APK is `mobile/scanner/build/app/outputs/flutter-apk/app-release.apk`.

## Part G. Install the app and test it on a phone (owner)

1. Use an Android phone with Google Play services (not a recent Huawei phone).
2. Copy `app-release.apk` to the phone with a USB cable, or email it to the phone.
3. On the phone, open the APK file. If Android asks, allow installs from that source, then
   select **Install**.
4. Open **PVM Scanner** and sign in with your work account.
5. Open **Settings** in the app. The line "Sign-in redirect URI (for IT)" must end with the same
   hash as step F3. If sign-in failed, the same text shows under the error.
6. Scan five real PODs:
   - Two signed PVM invoices.
   - Two client delivery notes or goods-received notes.
   - One long thin slip or one A3 paper.
7. After each document, the camera opens again. Each item in the list must change to
   **Received**.
8. Wait two minutes. Open the QA workbench at **Documents**. The five documents must be there.
   Each one is **Filed** or on the **Review list**.
9. Open each document and compare the matched invoice with the paper.
10. Tell Claude the result for each document: the paper type, the state, and whether the invoice
    is correct.

## Part H. Scanner test on hard paper shapes (owner)

The throwaway test app is `mobile-spike-apk/pod-scan-test.apk` in the worktree folder.

1. Install it in the same way as Part G, steps 2 and 3.
2. Open **POD Scan Test** and select **Scan**.
3. Scan one paper of each kind: A4, square, long thin slip, A3, creased, and one in poor light.
4. For each paper, note:
   - Did the scanner find the edges? (yes or no)
   - Did you move the corners? (yes or no)
   - Can you read the text in the PDF? (yes or no)
5. Send the results to Claude, together with 10 to 20 POD photos or PDFs. Put them in a folder
   and give Claude the path.

## Part I. QA shadow run (owner and Claude)

Run QA for at least one week with real PODs. Acumatica attachments stay in shadow mode: the
backend records each attachment that it would make, and changes nothing in Acumatica.

1. Scan the day's returned PODs with the app.
2. Each day, open **Documents > Review list** and resolve each document.
3. Each day, open **Documents**, filter on **Filed**, and check ten documents against the paper.
4. Note every document that the backend filed against the wrong invoice. Report each one to
   Claude at once, with its document number.
5. Before Write mode, Claude does the three items from the PR description:
   - The return-to-review guard for an attachment in progress.
   - The rule for an invoice number that is read but matches no invoice. This needs your
     decision.
   - The live check of the Acumatica shipment fields and of one real Claude call.

## Part J. Production

Do Part J only after the QA shadow run shows no wrong filings.

1. Do Part B in production, if you have not done it yet.
2. Start the production deploy:

   ```bash
   gh workflow run deploy-prod.yml --repo braven-pvm/pvm-backend -f confirm="DEPLOY PRODUCTION"
   ```

3. Approve the run on GitHub: **Actions > Deploy production > Review deployments >
   production > Approve and deploy**.
4. Read the production API address:

   ```bash
   az containerapp show --resource-group rg-pvm-integrations-prod --name ca-pvm-api-prod --query properties.configuration.ingress.fqdn -o tsv
   ```

5. Do Part F, steps 4 to 9, with the production API app registration, the vault
   `kv-pvm-intg-prod`, and `https://<the address from step 4>` as `API_BASE_URL`. Use the same
   release key.
6. Do Part E in the production workbench (`https://backoffice.pvm.co.za/admin/users`).
7. Install the production APK on the office phones.

Write mode (attachments in Acumatica) is a separate decision after the production shadow run.
