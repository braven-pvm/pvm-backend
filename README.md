# PVM Backend

Backend, integration, and reporting platform for Acumatica Cloud ERP and trading-partner workflows.

Current discovery focus:

- Acumatica Cloud ERP 2025 R2 integration paths.
- Shoprite REST Web Services V9.3 invoice upload.
- Shoprite invoice-upload MVP scope.
- Long-running integration service architecture and operations model.

## Local Quickstart

Start PostgreSQL:

```powershell
docker compose -f deploy/docker-compose.yml up -d
```

Apply database migrations:

```powershell
$env:ConnectionStrings__Pvm="Host=localhost;Port=54329;Database=pvm;Username=pvm;Password=pvm"
dotnet run --project backend/src/Pvm.DatabaseMigrator/Pvm.DatabaseMigrator.csproj
```

The API and workers do not create or alter schemas at runtime. Run the
migrator once per deployment before starting a new application revision.

Run backend checks:

```powershell
dotnet test backend/Pvm.sln
```

If the local machine does not have the .NET 10 SDK, use the SDK container:

```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build backend/Pvm.sln
docker run --rm -e TESTCONTAINERS_RYUK_DISABLED=true -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -v "${PWD}:/src" -v /var/run/docker.sock:/var/run/docker.sock -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test backend/Pvm.sln
```

Start the API:

```powershell
dotnet run --project backend/src/Pvm.Api/Pvm.Api.csproj
```

Start the invoice workbench:

```powershell
cd frontend/workbench
npm ci
npm run dev
```

Open the workbench:

```text
http://localhost:3000/invoices
```

For the current QA slice, use `POST /api/invoices/refresh` or the workbench refresh action to load the sanitized fixture invoice.

## Runbooks

- [Agent Onboarding](AGENTS.md)
- [Documentation Index](docs/README.md)
- [Current Shoprite Project Handoff](docs/handovers/2026-06-10-shoprite-project-handoff.md)
- [Shoprite QA Invoice Submission Runbook](docs/runbooks/shoprite-qa-submission.md)
- [Azure Container Apps Deployment Notes](deploy/azure-container-apps-notes.md)
- [Azure QA Infrastructure Provisioning Playbook](docs/runbooks/azure-qa-provisioning-playbook.md)

