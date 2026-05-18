# Shoprite Invoice Upload MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a QA-only vertical slice that reads finalized Acumatica invoices, validates them for Shoprite, generates Shoprite invoice XML, lets an operator manually submit one invoice at a time, and records full audit/submission history.

**Architecture:** Use a .NET 10 backend with focused domain modules, PostgreSQL persistence, a minimal Next.js workbench, and connector boundaries for Acumatica and Shoprite. Manual MVP submission and future automatic submission must use the same backend command path.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, Testcontainers, xUnit, Next.js, TypeScript, Docker Compose. Azure Container Apps, Azure Database for PostgreSQL, Service Bus, Blob Storage, and Key Vault are the target hosted services, but MVP local development starts with Docker Compose.

---

## Source Inputs

- `docs/spec-slices/shoprite-invoice-upload-mvp.md`
- `docs/shoprite-rest-v9.3-discovery.md`
- `docs/acumatica-2025-r2-integration-research.md`
- `docs/architecture-stack-options.md`
- `docs/specifications/Shoprite REST Web Services Guide V9.3.pdf`

## Delivery Principles

- Keep Shoprite and Acumatica connector details outside the domain model.
- Keep manual submission and future automatic submission on one command path.
- Database constraints enforce idempotency.
- No raw XML editing.
- No production Shoprite traffic in MVP.
- Every external request/response is captured with redacted credentials.
- Build thin vertical slices and commit after each task.

## Planned Repository Shape

```text
/
├── backend/
│   ├── Pvm.sln
│   ├── Directory.Build.props
│   ├── src/
│   │   ├── Pvm.Api/
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   └── Features/
│   │   │       ├── Invoices/
│   │   │       ├── Mappings/
│   │   │       └── Submissions/
│   │   ├── Pvm.Application/
│   │   │   ├── Invoices/
│   │   │   ├── Mappings/
│   │   │   ├── Shoprite/
│   │   │   ├── Acumatica/
│   │   │   └── Submissions/
│   │   ├── Pvm.Domain/
│   │   │   ├── Invoices/
│   │   │   ├── Mappings/
│   │   │   ├── Validation/
│   │   │   └── Submissions/
│   │   └── Pvm.Infrastructure/
│   │       ├── Persistence/
│   │       ├── Acumatica/
│   │       ├── Shoprite/
│   │       └── Payloads/
│   └── tests/
│       ├── Pvm.Domain.Tests/
│       ├── Pvm.Application.Tests/
│       └── Pvm.Infrastructure.Tests/
├── frontend/
│   └── workbench/
│       ├── package.json
│       ├── app/
│       │   ├── invoices/
│       │   ├── mappings/
│       │   └── layout.tsx
│       └── src/
│           ├── api/
│           ├── components/
│           └── types/
├── deploy/
│   ├── docker-compose.yml
│   └── env.example
└── docs/
```

## Task 1: Backend Solution Skeleton

**Files:**
- Create: `backend/Pvm.sln`
- Create: `backend/Directory.Build.props`
- Create: `backend/src/Pvm.Api/Pvm.Api.csproj`
- Create: `backend/src/Pvm.Api/Program.cs`
- Create: `backend/src/Pvm.Domain/Pvm.Domain.csproj`
- Create: `backend/src/Pvm.Application/Pvm.Application.csproj`
- Create: `backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj`
- Create: `backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj`
- Create: `backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj`
- Create: `backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj`

- [ ] **Step 1: Create the .NET solution and projects**

Run:

```powershell
New-Item -ItemType Directory -Force -Path backend/src, backend/tests | Out-Null
dotnet new sln -n Pvm -o backend
dotnet new webapi -n Pvm.Api -o backend/src/Pvm.Api --framework net10.0
dotnet new classlib -n Pvm.Domain -o backend/src/Pvm.Domain --framework net10.0
dotnet new classlib -n Pvm.Application -o backend/src/Pvm.Application --framework net10.0
dotnet new classlib -n Pvm.Infrastructure -o backend/src/Pvm.Infrastructure --framework net10.0
dotnet new xunit -n Pvm.Domain.Tests -o backend/tests/Pvm.Domain.Tests --framework net10.0
dotnet new xunit -n Pvm.Application.Tests -o backend/tests/Pvm.Application.Tests --framework net10.0
dotnet new xunit -n Pvm.Infrastructure.Tests -o backend/tests/Pvm.Infrastructure.Tests --framework net10.0
dotnet sln backend/Pvm.sln add backend/src/Pvm.Api/Pvm.Api.csproj backend/src/Pvm.Domain/Pvm.Domain.csproj backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj
dotnet add backend/src/Pvm.Application/Pvm.Application.csproj reference backend/src/Pvm.Domain/Pvm.Domain.csproj
dotnet add backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj reference backend/src/Pvm.Domain/Pvm.Domain.csproj backend/src/Pvm.Application/Pvm.Application.csproj
dotnet add backend/src/Pvm.Api/Pvm.Api.csproj reference backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj
dotnet add backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj reference backend/src/Pvm.Domain/Pvm.Domain.csproj
dotnet add backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj reference backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Domain/Pvm.Domain.csproj
dotnet add backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj reference backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Domain/Pvm.Domain.csproj
```

Expected: solution and all projects are created and reference each other.

- [ ] **Step 2: Add shared build settings**

Create `backend/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Make the API health endpoint explicit**

Replace `backend/src/Pvm.Api/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.Run();

public partial class Program;
```

- [ ] **Step 4: Verify build and test**

Run:

```powershell
dotnet build backend/Pvm.sln
dotnet test backend/Pvm.sln
```

Expected: both commands pass.

- [ ] **Step 5: Commit**

```powershell
git add backend
git commit -m "chore: scaffold backend solution"
```

## Task 2: Local Infrastructure and Configuration

**Files:**
- Create: `deploy/docker-compose.yml`
- Create: `deploy/env.example`
- Modify: `.gitignore`
- Modify: `backend/src/Pvm.Api/appsettings.json`
- Create: `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs`
- Create: `backend/src/Pvm.Infrastructure/Persistence/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add EF Core and PostgreSQL packages**

Run:

```powershell
dotnet add backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj package Microsoft.EntityFrameworkCore
dotnet add backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add backend/src/Pvm.Api/Pvm.Api.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj package Testcontainers.PostgreSql
dotnet add backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj package Microsoft.EntityFrameworkCore
dotnet add backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

- [ ] **Step 2: Add Docker Compose**

Create `deploy/docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:16
    container_name: pvm-postgres
    environment:
      POSTGRES_USER: pvm
      POSTGRES_PASSWORD: pvm
      POSTGRES_DB: pvm
    ports:
      - "54329:5432"
    volumes:
      - pvm-postgres:/var/lib/postgresql/data

volumes:
  pvm-postgres:
```

Create `deploy/env.example`:

```text
ConnectionStrings__Pvm=Host=localhost;Port=54329;Database=pvm;Username=pvm;Password=pvm
Acumatica__BaseUrl=https://example.acumatica.com
Acumatica__Tenant=
Acumatica__ClientId=
Acumatica__ClientSecret=
Shoprite__BaseUrl=https://externalservicesqa.shopriteholdings.co.za/b2bservice/api
Shoprite__Username=
Shoprite__Password=
Shoprite__ContractId=aa659aa2-4175-471f-8c82-59ca416723cf
Shoprite__UiUser=
```

- [ ] **Step 3: Keep local secrets ignored**

Ensure `.gitignore` contains:

```text
.env
.env.*
!.env.example
```

- [ ] **Step 4: Add DbContext shell**

Create `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
}
```

Create `backend/src/Pvm.Infrastructure/Persistence/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pvm.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPvmPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Pvm")
            ?? throw new InvalidOperationException("Connection string 'Pvm' is required.");

        services.AddDbContext<PvmDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
```

- [ ] **Step 5: Register persistence**

Modify `backend/src/Pvm.Api/Program.cs`:

```csharp
using Pvm.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddPvmPersistence(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.Run();

public partial class Program;
```

- [ ] **Step 6: Verify local infrastructure**

Run:

```powershell
docker compose -f deploy/docker-compose.yml up -d
dotnet build backend/Pvm.sln
```

Expected: Postgres container starts and solution builds.

- [ ] **Step 7: Commit**

```powershell
git add .gitignore deploy backend
git commit -m "chore: add local infrastructure configuration"
```

## Task 3: Domain Model and Validation Results

**Files:**
- Create: `backend/src/Pvm.Domain/Invoices/CanonicalInvoice.cs`
- Create: `backend/src/Pvm.Domain/Invoices/CanonicalInvoiceLine.cs`
- Create: `backend/src/Pvm.Domain/Invoices/Money.cs`
- Create: `backend/src/Pvm.Domain/Invoices/ShopriteMeasurementUnit.cs`
- Create: `backend/src/Pvm.Domain/Validation/ValidationIssue.cs`
- Create: `backend/src/Pvm.Domain/Validation/ValidationSeverity.cs`
- Create: `backend/src/Pvm.Domain/Validation/ValidationResult.cs`
- Test: `backend/tests/Pvm.Domain.Tests/ValidationResultTests.cs`

- [ ] **Step 1: Write validation result tests**

Create `backend/tests/Pvm.Domain.Tests/ValidationResultTests.cs`:

```csharp
using Pvm.Domain.Validation;

namespace Pvm.Domain.Tests;

public sealed class ValidationResultTests
{
    [Fact]
    public void CanSubmit_is_false_when_any_blocking_issue_exists()
    {
        var result = new ValidationResult([
            new ValidationIssue("missing-gln", "Store/DC GLN is missing.", ValidationSeverity.Blocking, "integration-config")
        ]);

        Assert.False(result.CanSubmit);
    }

    [Fact]
    public void CanSubmit_is_true_when_result_has_only_warnings()
    {
        var result = new ValidationResult([
            new ValidationIssue("unverified-uom", "UOM mapping is unverified in QA.", ValidationSeverity.Warning, "integration-config")
        ]);

        Assert.True(result.CanSubmit);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj --filter ValidationResultTests
```

Expected: fails because validation types do not exist.

- [ ] **Step 3: Add domain records**

Create `backend/src/Pvm.Domain/Invoices/Money.cs`:

```csharp
namespace Pvm.Domain.Invoices;

public sealed record Money(string CurrencyCode, decimal Amount);
```

Create `backend/src/Pvm.Domain/Invoices/ShopriteMeasurementUnit.cs`:

```csharp
namespace Pvm.Domain.Invoices;

public enum ShopriteMeasurementUnit
{
    EA,
    CA,
    CS,
    KG
}
```

Create `backend/src/Pvm.Domain/Invoices/CanonicalInvoiceLine.cs`:

```csharp
namespace Pvm.Domain.Invoices;

public sealed record CanonicalInvoiceLine(
    int LineNumber,
    string AcumaticaInventoryId,
    string? Gtin,
    string Description,
    decimal Quantity,
    string AcumaticaUom,
    ShopriteMeasurementUnit? ShopriteUom,
    decimal? PackSize,
    Money UnitAmountExcludingTax,
    Money UnitAmountIncludingTax,
    Money TaxAmount,
    string? TaxCategoryCode,
    decimal? TaxPercentage,
    bool IsCatchWeight);
```

Create `backend/src/Pvm.Domain/Invoices/CanonicalInvoice.cs`:

```csharp
namespace Pvm.Domain.Invoices;

public sealed record CanonicalInvoice(
    string AcumaticaInvoiceId,
    string InvoiceNumber,
    string CustomerAccount,
    string? CustomerLocation,
    string? ShopritePurchaseOrderNumber,
    string? SupplierGln,
    string? StoreDcGln,
    string CountryCode,
    string CurrencyCode,
    DateTimeOffset InvoiceDate,
    Money TotalExcludingTax,
    Money TotalIncludingTax,
    Money TotalTax,
    IReadOnlyList<CanonicalInvoiceLine> Lines);
```

Create `backend/src/Pvm.Domain/Validation/ValidationSeverity.cs`:

```csharp
namespace Pvm.Domain.Validation;

public enum ValidationSeverity
{
    Warning,
    Blocking
}
```

Create `backend/src/Pvm.Domain/Validation/ValidationIssue.cs`:

```csharp
namespace Pvm.Domain.Validation;

public sealed record ValidationIssue(
    string Code,
    string Message,
    ValidationSeverity Severity,
    string FixLocation);
```

Create `backend/src/Pvm.Domain/Validation/ValidationResult.cs`:

```csharp
namespace Pvm.Domain.Validation;

public sealed class ValidationResult(IReadOnlyList<ValidationIssue> issues)
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;

    public bool CanSubmit => Issues.All(issue => issue.Severity != ValidationSeverity.Blocking);
}
```

- [ ] **Step 4: Verify tests**

Run:

```powershell
dotnet test backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj --filter ValidationResultTests
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Domain backend/tests/Pvm.Domain.Tests
git commit -m "feat: add canonical invoice validation model"
```

## Task 4: Shoprite Invoice Validation

**Files:**
- Create: `backend/src/Pvm.Domain/Validation/ShopriteInvoiceValidator.cs`
- Test: `backend/tests/Pvm.Domain.Tests/ShopriteInvoiceValidatorTests.cs`

- [ ] **Step 1: Write validator tests**

Create `backend/tests/Pvm.Domain.Tests/ShopriteInvoiceValidatorTests.cs` with tests for:

- missing PO blocks submission
- missing DC GLN blocks submission
- non-ZAR blocks submission
- catch weight line blocks submission
- unverified UOM is warning in QA and blocking in production
- zero quantity line blocks submission
- totals mismatch blocks submission

Use this helper in the test file:

```csharp
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Domain.Tests;

public sealed class ShopriteInvoiceValidatorTests
{
    [Fact]
    public void Missing_purchase_order_blocks_submission()
    {
        var invoice = ValidInvoice() with { ShopritePurchaseOrderNumber = null };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        Assert.Contains(result.Issues, issue => issue.Code == "missing-shoprite-po" && issue.Severity == ValidationSeverity.Blocking);
        Assert.False(result.CanSubmit);
    }

    private static CanonicalInvoice ValidInvoice()
    {
        return new CanonicalInvoice(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV0001",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 100m),
            TotalIncludingTax: new Money("ZAR", 115m),
            TotalTax: new Money("ZAR", 15m),
            Lines:
            [
                new CanonicalInvoiceLine(
                    LineNumber: 1,
                    AcumaticaInventoryId: "SKU-1",
                    Gtin: "16001069205048",
                    Description: "Item 1",
                    Quantity: 1m,
                    AcumaticaUom: "EA",
                    ShopriteUom: ShopriteMeasurementUnit.EA,
                    PackSize: 1m,
                    UnitAmountExcludingTax: new Money("ZAR", 100m),
                    UnitAmountIncludingTax: new Money("ZAR", 115m),
                    TaxAmount: new Money("ZAR", 15m),
                    TaxCategoryCode: "STANDARD",
                    TaxPercentage: 15m,
                    IsCatchWeight: false)
            ]);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj --filter ShopriteInvoiceValidatorTests
```

Expected: fails because `ShopriteInvoiceValidator` does not exist.

- [ ] **Step 3: Implement validator**

Create `backend/src/Pvm.Domain/Validation/ShopriteInvoiceValidator.cs`:

```csharp
using Pvm.Domain.Invoices;

namespace Pvm.Domain.Validation;

public enum ShopriteValidationEnvironment
{
    Qa,
    Production
}

public static class ShopriteInvoiceValidator
{
    public static ValidationResult Validate(CanonicalInvoice invoice, ShopriteValidationEnvironment environment)
    {
        var issues = new List<ValidationIssue>();

        Require(invoice.ShopritePurchaseOrderNumber, "missing-shoprite-po", "Shoprite PO number is required.", "Acumatica", issues);
        Require(invoice.SupplierGln, "missing-supplier-gln", "Supplier GLN is required.", "integration-config", issues);
        Require(invoice.StoreDcGln, "missing-store-dc-gln", "Store/DC GLN is required.", "integration-config", issues);

        if (invoice.CountryCode != "ZA")
        {
            issues.Add(Block("unsupported-country", "MVP supports South Africa only.", "Acumatica"));
        }

        if (invoice.CurrencyCode != "ZAR")
        {
            issues.Add(Block("unsupported-currency", "MVP supports ZAR only.", "Acumatica"));
        }

        foreach (var line in invoice.Lines)
        {
            if (line.Quantity <= 0)
            {
                issues.Add(Block("zero-quantity-line", $"Line {line.LineNumber} has zero or negative quantity.", "Acumatica"));
            }

            Require(line.Gtin, "missing-gtin", $"Line {line.LineNumber} is missing GTIN.", "integration-config", issues);

            if (line.ShopriteUom is null)
            {
                issues.Add(Block("missing-shoprite-uom", $"Line {line.LineNumber} has no Shoprite UOM mapping.", "integration-config"));
            }

            if (line.IsCatchWeight)
            {
                issues.Add(Block("catch-weight-unsupported", $"Line {line.LineNumber} is catch weight and is excluded from MVP.", "Acumatica"));
            }
        }

        var lineExcluding = invoice.Lines.Sum(line => decimal.Round(line.UnitAmountExcludingTax.Amount * line.Quantity, 2));
        var lineIncluding = invoice.Lines.Sum(line => decimal.Round(line.UnitAmountIncludingTax.Amount * line.Quantity, 2));
        var lineTax = invoice.Lines.Sum(line => decimal.Round(line.TaxAmount.Amount * line.Quantity, 2));

        if (lineExcluding != invoice.TotalExcludingTax.Amount ||
            lineIncluding != invoice.TotalIncludingTax.Amount ||
            lineTax != invoice.TotalTax.Amount)
        {
            issues.Add(Block("totals-mismatch", "Generated line totals do not match invoice totals.", "Acumatica"));
        }

        return new ValidationResult(issues);
    }

    private static void Require(string? value, string code, string message, string fixLocation, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Block(code, message, fixLocation));
        }
    }

    private static ValidationIssue Block(string code, string message, string fixLocation)
        => new(code, message, ValidationSeverity.Blocking, fixLocation);
}
```

- [ ] **Step 4: Verify tests**

Run:

```powershell
dotnet test backend/tests/Pvm.Domain.Tests/Pvm.Domain.Tests.csproj --filter ShopriteInvoiceValidatorTests
```

Expected: pass after completing all tests in Step 1.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Domain/Validation backend/tests/Pvm.Domain.Tests
git commit -m "feat: validate Shoprite invoice requirements"
```

## Task 5: Persistence Schema for MVP State

**Files:**
- Create: `backend/src/Pvm.Infrastructure/Persistence/Entities/InvoiceCandidateEntity.cs`
- Create: `backend/src/Pvm.Infrastructure/Persistence/Entities/InvoiceSubmissionAttemptEntity.cs`
- Create: `backend/src/Pvm.Infrastructure/Persistence/Entities/AuditEventEntity.cs`
- Modify: `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs`
- Test: `backend/tests/Pvm.Infrastructure.Tests/Persistence/InvoicePersistenceTests.cs`

- [ ] **Step 1: Write persistence test for idempotency key uniqueness**

Create `backend/tests/Pvm.Infrastructure.Tests/Persistence/InvoicePersistenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class InvoicePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Invoice_candidate_idempotency_key_is_unique()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.InvoiceCandidates.Add(NewCandidate("key-1", "INV001"));
        db.InvoiceCandidates.Add(NewCandidate("key-1", "INV002"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

    private static InvoiceCandidateEntity NewCandidate(string key, string invoiceNumber)
        => new()
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = invoiceNumber,
            InvoiceNumber = invoiceNumber,
            CustomerAccount = "SHOPRITE",
            IdempotencyKey = key,
            Status = "Candidate",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```powershell
dotnet test backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj --filter InvoicePersistenceTests
```

Expected: fails because entities do not exist.

- [ ] **Step 3: Add persistence entities**

Create `backend/src/Pvm.Infrastructure/Persistence/Entities/InvoiceCandidateEntity.cs`:

```csharp
namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class InvoiceCandidateEntity
{
    public Guid Id { get; set; }
    public required string AcumaticaInvoiceId { get; set; }
    public required string InvoiceNumber { get; set; }
    public required string CustomerAccount { get; set; }
    public string? CustomerLocation { get; set; }
    public string? ShopritePurchaseOrderNumber { get; set; }
    public string? SupplierGln { get; set; }
    public string? StoreDcGln { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Status { get; set; }
    public string? ValidationJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Create `backend/src/Pvm.Infrastructure/Persistence/Entities/InvoiceSubmissionAttemptEntity.cs`:

```csharp
namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class InvoiceSubmissionAttemptEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public required string InitiatedBy { get; set; }
    public required string InitiationMode { get; set; }
    public required string Status { get; set; }
    public string? RequestPayloadLocation { get; set; }
    public string? RequestPayloadHash { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponsePayloadLocation { get; set; }
    public string? ResponsePayloadHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `backend/src/Pvm.Infrastructure/Persistence/Entities/AuditEventEntity.cs`:

```csharp
namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }
    public required string Actor { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 4: Configure DbContext**

Modify `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceCandidateEntity> InvoiceCandidates => Set<InvoiceCandidateEntity>();
    public DbSet<InvoiceSubmissionAttemptEntity> InvoiceSubmissionAttempts => Set<InvoiceSubmissionAttemptEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoiceCandidateEntity>(entity =>
        {
            entity.ToTable("invoice_candidates");
            entity.HasKey(candidate => candidate.Id);
            entity.HasIndex(candidate => candidate.IdempotencyKey).IsUnique();
            entity.Property(candidate => candidate.IdempotencyKey).HasMaxLength(512);
            entity.Property(candidate => candidate.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<InvoiceSubmissionAttemptEntity>(entity =>
        {
            entity.ToTable("invoice_submission_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InvoiceCandidateId);
            entity.Property(attempt => attempt.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });
        });
    }
}
```

- [ ] **Step 5: Verify persistence tests**

Run:

```powershell
dotnet test backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj --filter InvoicePersistenceTests
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add backend/src/Pvm.Infrastructure backend/tests/Pvm.Infrastructure.Tests
git commit -m "feat: persist invoice submission state"
```

## Task 6: Shoprite XML Generator

**Files:**
- Create: `backend/src/Pvm.Application/Shoprite/ShopriteInvoiceXmlGenerator.cs`
- Test: `backend/tests/Pvm.Application.Tests/Shoprite/ShopriteInvoiceXmlGeneratorTests.cs`

- [ ] **Step 1: Write XML generator snapshot-style test**

Create `backend/tests/Pvm.Application.Tests/Shoprite/ShopriteInvoiceXmlGeneratorTests.cs`:

```csharp
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;

namespace Pvm.Application.Tests.Shoprite;

public sealed class ShopriteInvoiceXmlGeneratorTests
{
    [Fact]
    public void Generate_includes_required_invoice_identity_fields()
    {
        var invoice = new CanonicalInvoice(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV342699282",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 109.8765m),
            TotalIncludingTax: new Money("ZAR", 125.1789m),
            TotalTax: new Money("ZAR", 15.3024m),
            Lines:
            [
                new CanonicalInvoiceLine(1, "SKU-1", "16001069205048", "Item 1", 1m, "EA", ShopriteMeasurementUnit.EA, 24m, new Money("ZAR", 109.8765m), new Money("ZAR", 125.1789m), new Money("ZAR", 15.3024m), "STANDARD", 15m, false)
            ]);

        var xml = ShopriteInvoiceXmlGenerator.Generate(invoice);

        Assert.Contains("<invoiceMessage", xml);
        Assert.Contains("<InstanceIdentifier>INV342699282</InstanceIdentifier>", xml);
        Assert.Contains("<entityIdentification>3869384391</entityIdentification>", xml);
        Assert.Contains("<gtin>16001069205048</gtin>", xml);
        Assert.Contains("measurementUnitCode=\"EA\"", xml);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```powershell
dotnet test backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj --filter ShopriteInvoiceXmlGeneratorTests
```

Expected: fails because generator does not exist.

- [ ] **Step 3: Implement XML generator**

Create `backend/src/Pvm.Application/Shoprite/ShopriteInvoiceXmlGenerator.cs`:

```csharp
using System.Globalization;
using System.Security;
using System.Text;
using Pvm.Domain.Invoices;

namespace Pvm.Application.Shoprite;

public static class ShopriteInvoiceXmlGenerator
{
    public static string Generate(CanonicalInvoice invoice)
    {
        var createdAt = invoice.InvoiceDate.ToString("O", CultureInfo.InvariantCulture);
        var effectiveDate = invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var supplierGln = Escape(invoice.SupplierGln ?? string.Empty);
        var storeDcGln = Escape(invoice.StoreDcGln ?? string.Empty);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        builder.AppendLine("""<invoiceMessage xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns="urn:gs1:ecom:invoice:xsd:3">""");
        builder.AppendLine("""<StandardBusinessDocumentHeader xmlns="http://www.unece.org/cefact/namespaces/StandardBusinessDocumentHeader">""");
        builder.AppendLine("<HeaderVersion>3.2.0</HeaderVersion>");
        builder.AppendLine($"<Sender><Identifier Authority=\"SenderEAN\">{supplierGln}</Identifier></Sender>");
        builder.AppendLine($"<Receiver><Identifier Authority=\"ReceiverEAN\">{storeDcGln}</Identifier></Receiver>");
        builder.AppendLine("<DocumentIdentification>");
        builder.AppendLine("<Standard>Standard</Standard>");
        builder.AppendLine("<TypeVersion>3.2.0</TypeVersion>");
        builder.AppendLine($"<InstanceIdentifier>{Escape(invoice.InvoiceNumber)}</InstanceIdentifier>");
        builder.AppendLine("<Type>Invoice</Type>");
        builder.AppendLine("<MultipleType>false</MultipleType>");
        builder.AppendLine($"<CreationDateAndTime>{createdAt}</CreationDateAndTime>");
        builder.AppendLine("</DocumentIdentification>");
        builder.AppendLine("<Manifest />");
        builder.AppendLine("</StandardBusinessDocumentHeader>");
        builder.AppendLine("<invoice xmlns=\"\">");
        builder.AppendLine($"<creationDateTime>{createdAt}</creationDateTime>");
        builder.AppendLine("<documentStatusCode>ORIGINAL</documentStatusCode>");
        builder.AppendLine("<documentActionCode>ADD</documentActionCode>");
        builder.AppendLine("<documentStructureVersion>3.2.0</documentStructureVersion>");
        builder.AppendLine("<revisionNumber>1.0</revisionNumber>");
        builder.AppendLine($"<documentEffectiveDate><date>{effectiveDate}</date></documentEffectiveDate>");
        builder.AppendLine($"<InvoiceIdentification><entityIdentification>{Escape(invoice.InvoiceNumber)}</entityIdentification><contentOwner><gln>{supplierGln}</gln></contentOwner></InvoiceIdentification>");
        builder.AppendLine("<invoiceType>INVOICE</invoiceType>");
        builder.AppendLine($"<invoiceCurrencyCode>{Escape(invoice.CurrencyCode)}</invoiceCurrencyCode>");
        builder.AppendLine($"<countryOfSupplyOfGoods>{Escape(invoice.CountryCode)}</countryOfSupplyOfGoods>");
        builder.AppendLine($"<buyer><gln>{storeDcGln}</gln></buyer>");
        builder.AppendLine($"<seller><gln>{supplierGln}</gln></seller>");
        builder.AppendLine($"<shipTo><gln>{storeDcGln}</gln></shipTo>");
        builder.AppendLine("<invoiceTotals>");
        builder.AppendLine($"<totalInvoiceAmount currencyCode=\"{Escape(invoice.CurrencyCode)}\">{Amount(invoice.TotalExcludingTax.Amount)}</totalInvoiceAmount>");
        builder.AppendLine($"<totalInvoiceAmountPayable currencyCode=\"{Escape(invoice.CurrencyCode)}\">{Amount(invoice.TotalIncludingTax.Amount)}</totalInvoiceAmountPayable>");
        builder.AppendLine($"<totalVATAmount currencyCode=\"{Escape(invoice.CurrencyCode)}\">{Amount(invoice.TotalTax.Amount)}</totalVATAmount>");
        builder.AppendLine("</invoiceTotals>");
        builder.AppendLine($"<purchaseOrder><entityIdentification>{Escape(invoice.ShopritePurchaseOrderNumber ?? string.Empty)}</entityIdentification></purchaseOrder>");

        foreach (var line in invoice.Lines)
        {
            builder.AppendLine("<invoiceLineItem>");
            builder.AppendLine($"<lineItemNumber>{line.LineNumber}</lineItemNumber>");
            builder.AppendLine($"<invoicedQuantity>{Quantity(line.Quantity)}</invoicedQuantity>");
            builder.AppendLine($"<amountExclusiveAllowancesCharges currencyCode=\"{Escape(line.UnitAmountExcludingTax.CurrencyCode)}\">{Amount(line.UnitAmountExcludingTax.Amount)}</amountExclusiveAllowancesCharges>");
            builder.AppendLine($"<amountInclusiveAllowancesCharges currencyCode=\"{Escape(line.UnitAmountIncludingTax.CurrencyCode)}\">{Amount(line.UnitAmountIncludingTax.Amount)}</amountInclusiveAllowancesCharges>");
            builder.AppendLine($"<transferOfOwnershipDate>{effectiveDate}</transferOfOwnershipDate>");
            builder.AppendLine($"<note languageCode=\"EN\">{Escape(line.Description)}</note>");
            builder.AppendLine("<transactionalTradeItem>");
            builder.AppendLine($"<gtin>{Escape(line.Gtin ?? string.Empty)}</gtin>");
            builder.AppendLine("<transactionalItemData><transactionalItemWeight>");
            builder.AppendLine($"<measurementValue measurementUnitCode=\"{line.ShopriteUom}\">0</measurementValue>");
            builder.AppendLine("</transactionalItemWeight></transactionalItemData>");
            builder.AppendLine($"<size><sizeCode>{Quantity(line.PackSize ?? 1m)}</sizeCode></size>");
            builder.AppendLine("</transactionalTradeItem>");
            builder.AppendLine("<invoiceLineTaxInformation>");
            builder.AppendLine($"<dutyFeeTaxAmount currencyCode=\"{Escape(line.TaxAmount.CurrencyCode)}\">{Amount(line.TaxAmount.Amount)}</dutyFeeTaxAmount>");
            builder.AppendLine($"<dutyFeeTaxCategoryCode>{Escape(line.TaxCategoryCode ?? string.Empty)}</dutyFeeTaxCategoryCode>");
            builder.AppendLine($"<dutyFeeTaxPercentage>{Quantity(line.TaxPercentage ?? 0m)}</dutyFeeTaxPercentage>");
            builder.AppendLine("<dutyFeeTaxTypeCode>VAT</dutyFeeTaxTypeCode>");
            builder.AppendLine("</invoiceLineTaxInformation>");
            builder.AppendLine("</invoiceLineItem>");
        }

        builder.AppendLine("</invoice>");
        builder.AppendLine("</invoiceMessage>");
        return builder.ToString();
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
    private static string Amount(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Verify XML test**

Run:

```powershell
dotnet test backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj --filter ShopriteInvoiceXmlGeneratorTests
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Application backend/tests/Pvm.Application.Tests
git commit -m "feat: generate Shoprite invoice XML"
```

## Task 7: Acumatica Invoice Candidate Adapter

**Files:**
- Create: `backend/src/Pvm.Application/Acumatica/AcumaticaInvoiceDto.cs`
- Create: `backend/src/Pvm.Application/Acumatica/AcumaticaInvoiceNormalizer.cs`
- Test: `backend/tests/Pvm.Application.Tests/Acumatica/AcumaticaInvoiceNormalizerTests.cs`

- [ ] **Step 1: Write normalizer test**

Create `backend/tests/Pvm.Application.Tests/Acumatica/AcumaticaInvoiceNormalizerTests.cs`:

```csharp
using Pvm.Application.Acumatica;

namespace Pvm.Application.Tests.Acumatica;

public sealed class AcumaticaInvoiceNormalizerTests
{
    [Fact]
    public void Normalize_maps_finalized_invoice_to_canonical_invoice()
    {
        var dto = new AcumaticaInvoiceDto(
            Id: "a1",
            InvoiceNumber: "INV001",
            Status: "Released",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "BRACKENFELL-DC",
            CustomerOrder: "3869384391",
            CurrencyCode: "ZAR",
            CountryCode: "ZA",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: 100m,
            TotalIncludingTax: 115m,
            TotalTax: 15m,
            Lines:
            [
                new AcumaticaInvoiceLineDto(1, "SKU-1", "16001069205048", "Item 1", 1m, "EA", 1m, 100m, 115m, 15m, "STANDARD", 15m, false)
            ]);

        var invoice = AcumaticaInvoiceNormalizer.Normalize(dto, supplierGln: "9999999999999", storeDcGln: "6001001018104");

        Assert.Equal("INV001", invoice.InvoiceNumber);
        Assert.Equal("3869384391", invoice.ShopritePurchaseOrderNumber);
        Assert.Equal("6001001018104", invoice.StoreDcGln);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```powershell
dotnet test backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj --filter AcumaticaInvoiceNormalizerTests
```

Expected: fails because normalizer does not exist.

- [ ] **Step 3: Implement normalizer**

Create `backend/src/Pvm.Application/Acumatica/AcumaticaInvoiceDto.cs`:

```csharp
namespace Pvm.Application.Acumatica;

public sealed record AcumaticaInvoiceDto(
    string Id,
    string InvoiceNumber,
    string Status,
    string CustomerAccount,
    string? CustomerLocation,
    string? CustomerOrder,
    string CurrencyCode,
    string CountryCode,
    DateTimeOffset InvoiceDate,
    decimal TotalExcludingTax,
    decimal TotalIncludingTax,
    decimal TotalTax,
    IReadOnlyList<AcumaticaInvoiceLineDto> Lines);

public sealed record AcumaticaInvoiceLineDto(
    int LineNumber,
    string InventoryId,
    string? Gtin,
    string Description,
    decimal Quantity,
    string Uom,
    decimal? PackSize,
    decimal UnitAmountExcludingTax,
    decimal UnitAmountIncludingTax,
    decimal TaxAmount,
    string? TaxCategoryCode,
    decimal? TaxPercentage,
    bool IsCatchWeight);
```

Create `backend/src/Pvm.Application/Acumatica/AcumaticaInvoiceNormalizer.cs`:

```csharp
using Pvm.Domain.Invoices;

namespace Pvm.Application.Acumatica;

public static class AcumaticaInvoiceNormalizer
{
    public static CanonicalInvoice Normalize(
        AcumaticaInvoiceDto dto,
        string? supplierGln,
        string? storeDcGln)
    {
        return new CanonicalInvoice(
            AcumaticaInvoiceId: dto.Id,
            InvoiceNumber: dto.InvoiceNumber,
            CustomerAccount: dto.CustomerAccount,
            CustomerLocation: dto.CustomerLocation,
            ShopritePurchaseOrderNumber: dto.CustomerOrder,
            SupplierGln: supplierGln,
            StoreDcGln: storeDcGln,
            CountryCode: dto.CountryCode,
            CurrencyCode: dto.CurrencyCode,
            InvoiceDate: dto.InvoiceDate,
            TotalExcludingTax: new Money(dto.CurrencyCode, dto.TotalExcludingTax),
            TotalIncludingTax: new Money(dto.CurrencyCode, dto.TotalIncludingTax),
            TotalTax: new Money(dto.CurrencyCode, dto.TotalTax),
            Lines: dto.Lines.Select(line => new CanonicalInvoiceLine(
                LineNumber: line.LineNumber,
                AcumaticaInventoryId: line.InventoryId,
                Gtin: line.Gtin,
                Description: line.Description,
                Quantity: line.Quantity,
                AcumaticaUom: line.Uom,
                ShopriteUom: MapUom(line.Uom),
                PackSize: line.PackSize,
                UnitAmountExcludingTax: new Money(dto.CurrencyCode, line.UnitAmountExcludingTax),
                UnitAmountIncludingTax: new Money(dto.CurrencyCode, line.UnitAmountIncludingTax),
                TaxAmount: new Money(dto.CurrencyCode, line.TaxAmount),
                TaxCategoryCode: line.TaxCategoryCode,
                TaxPercentage: line.TaxPercentage,
                IsCatchWeight: line.IsCatchWeight)).ToList());
    }

    private static ShopriteMeasurementUnit? MapUom(string uom)
        => Enum.TryParse<ShopriteMeasurementUnit>(uom, ignoreCase: true, out var mapped) ? mapped : null;
}
```

- [ ] **Step 4: Verify test**

Run:

```powershell
dotnet test backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj --filter AcumaticaInvoiceNormalizerTests
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Application backend/tests/Pvm.Application.Tests
git commit -m "feat: normalize Acumatica invoice candidates"
```

## Task 8: Submission Command and Attempt Recording

**Files:**
- Create: `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceCommand.cs`
- Create: `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceResult.cs`
- Create: `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceHandler.cs`
- Create: `backend/src/Pvm.Application/Submissions/IShopriteInvoiceClient.cs`
- Create: `backend/src/Pvm.Application/Submissions/IInvoiceCandidateRepository.cs`
- Test: `backend/tests/Pvm.Application.Tests/Submissions/SubmitShopriteInvoiceHandlerTests.cs`

- [ ] **Step 1: Write command handler tests**

Create tests that prove:

- invalid invoice is not sent to Shoprite
- valid invoice calls `IShopriteInvoiceClient`
- timeout/unknown outcome returns `Ambiguous`
- duplicate key already submitted returns `DuplicateBlocked`

- [ ] **Step 2: Implement command contracts**

Create `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceCommand.cs`:

```csharp
namespace Pvm.Application.Submissions;

public sealed record SubmitShopriteInvoiceCommand(
    Guid InvoiceCandidateId,
    string InitiatedBy,
    string InitiationMode);
```

Create `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceResult.cs`:

```csharp
namespace Pvm.Application.Submissions;

public enum SubmitShopriteInvoiceStatus
{
    Submitted,
    ValidationBlocked,
    DuplicateBlocked,
    Ambiguous,
    Failed
}

public sealed record SubmitShopriteInvoiceResult(
    SubmitShopriteInvoiceStatus Status,
    string Message);
```

Create interfaces:

```csharp
namespace Pvm.Application.Submissions;

public interface IShopriteInvoiceClient
{
    Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken);
}

public sealed record ShopriteInvoiceResponse(bool Success, int? StatusCode, string Body, bool IsAmbiguous);
```

```csharp
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Submissions;

public interface IInvoiceCandidateRepository
{
    Task<CanonicalInvoice?> GetCanonicalInvoiceAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);
    Task<ValidationResult> GetValidationResultAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);
    Task<bool> HasSuccessfulSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);
    Task RecordAttemptAsync(Guid invoiceCandidateId, string initiatedBy, string initiationMode, string xml, ShopriteInvoiceResponse response, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Implement handler**

Create `backend/src/Pvm.Application/Submissions/SubmitShopriteInvoiceHandler.cs`:

```csharp
using Pvm.Application.Shoprite;

namespace Pvm.Application.Submissions;

public sealed class SubmitShopriteInvoiceHandler(
    IInvoiceCandidateRepository repository,
    IShopriteInvoiceClient shopriteClient)
{
    public async Task<SubmitShopriteInvoiceResult> HandleAsync(
        SubmitShopriteInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetCanonicalInvoiceAsync(command.InvoiceCandidateId, cancellationToken);
        if (invoice is null)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Failed, "Invoice candidate not found.");
        }

        var validation = await repository.GetValidationResultAsync(command.InvoiceCandidateId, cancellationToken);
        if (!validation.CanSubmit)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.ValidationBlocked, "Invoice has blocking validation issues.");
        }

        if (await repository.HasSuccessfulSubmissionAsync(command.InvoiceCandidateId, cancellationToken))
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.DuplicateBlocked, "Invoice already has a successful submission.");
        }

        var xml = ShopriteInvoiceXmlGenerator.Generate(invoice);
        var response = await shopriteClient.SubmitAsync(xml, cancellationToken);
        await repository.RecordAttemptAsync(command.InvoiceCandidateId, command.InitiatedBy, command.InitiationMode, xml, response, cancellationToken);

        if (response.IsAmbiguous)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Ambiguous, "Submission outcome is ambiguous and requires manual review.");
        }

        return response.Success
            ? new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Submitted, "Invoice submitted to Shoprite.")
            : new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Failed, "Shoprite rejected or failed the submission.");
    }
}
```

- [ ] **Step 4: Verify tests**

Run:

```powershell
dotnet test backend/tests/Pvm.Application.Tests/Pvm.Application.Tests.csproj --filter SubmitShopriteInvoiceHandlerTests
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Application backend/tests/Pvm.Application.Tests
git commit -m "feat: add Shoprite invoice submission command"
```

## Task 9: Shoprite VendorInvoice HTTP Client

**Files:**
- Create: `backend/src/Pvm.Infrastructure/Shoprite/ShopriteOptions.cs`
- Create: `backend/src/Pvm.Infrastructure/Shoprite/ShopriteInvoiceClient.cs`
- Create: `backend/src/Pvm.Infrastructure/Shoprite/ServiceCollectionExtensions.cs`
- Test: `backend/tests/Pvm.Infrastructure.Tests/Shoprite/ShopriteInvoiceClientTests.cs`

- [ ] **Step 1: Write HTTP client tests with fake handler**

Test that:

- Basic auth header is set.
- `ContractID` and `UIUser` headers are set.
- timeout exception maps to ambiguous response.
- non-success response captures response body.

- [ ] **Step 2: Implement options**

Create `backend/src/Pvm.Infrastructure/Shoprite/ShopriteOptions.cs`:

```csharp
namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteOptions
{
    public required string BaseUrl { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string ContractId { get; init; }
    public required string UiUser { get; init; }
}
```

- [ ] **Step 3: Implement client**

Create `backend/src/Pvm.Infrastructure/Shoprite/ShopriteInvoiceClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInvoiceClient(HttpClient httpClient, IOptions<ShopriteOptions> options)
    : IShopriteInvoiceClient
{
    private readonly ShopriteOptions _options = options.Value;

    public async Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "VendorInvoice");
        request.Content = new StringContent(xml, Encoding.UTF8, "application/xml");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        request.Headers.Add("ContractID", _options.ContractId);
        request.Headers.Add("UIUser", _options.UiUser);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ShopriteInvoiceResponse(response.IsSuccessStatusCode, (int)response.StatusCode, body, IsAmbiguous: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ShopriteInvoiceResponse(false, null, "Shoprite request timed out.", IsAmbiguous: true);
        }
        catch (HttpRequestException exception)
        {
            return new ShopriteInvoiceResponse(false, null, exception.Message, IsAmbiguous: true);
        }
    }
}
```

- [ ] **Step 4: Register client**

Create `backend/src/Pvm.Infrastructure/Shoprite/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.Shoprite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShopriteClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShopriteOptions>(configuration.GetSection("Shoprite"));
        services.AddHttpClient<IShopriteInvoiceClient, ShopriteInvoiceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ShopriteOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(300);
        });

        return services;
    }
}
```

- [ ] **Step 5: Verify tests**

Run:

```powershell
dotnet test backend/tests/Pvm.Infrastructure.Tests/Pvm.Infrastructure.Tests.csproj --filter ShopriteInvoiceClientTests
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add backend/src/Pvm.Infrastructure backend/tests/Pvm.Infrastructure.Tests
git commit -m "feat: add Shoprite VendorInvoice client"
```

## Task 10: Minimal API Endpoints for Workbench

**Files:**
- Create: `backend/src/Pvm.Api/Features/Invoices/InvoiceEndpoints.cs`
- Create: `backend/src/Pvm.Api/Features/Submissions/SubmissionEndpoints.cs`
- Modify: `backend/src/Pvm.Api/Program.cs`
- Test: `backend/tests/Pvm.Application.Tests` for handlers; optional API integration test later.

- [ ] **Step 1: Define MVP endpoints**

Add endpoints:

```text
GET /api/invoices/candidates
GET /api/invoices/candidates/{id}
POST /api/invoices/refresh
POST /api/invoices/{id}/revalidate
POST /api/invoices/{id}/submit
GET /api/invoices/{id}/attempts
```

- [ ] **Step 2: Implement endpoint extension shell**

Create `backend/src/Pvm.Api/Features/Invoices/InvoiceEndpoints.cs`:

```csharp
namespace Pvm.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapGet("/candidates", () => Results.Ok(Array.Empty<object>()));
        group.MapGet("/candidates/{id:guid}", (Guid id) => Results.Ok(new { id }));
        group.MapPost("/refresh", () => Results.Accepted());
        group.MapPost("/{id:guid}/revalidate", (Guid id) => Results.Ok(new { id, status = "validated" }));

        return app;
    }
}
```

Create `backend/src/Pvm.Api/Features/Submissions/SubmissionEndpoints.cs`:

```csharp
namespace Pvm.Api.Features.Submissions;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapPost("/{id:guid}/submit", (Guid id) => Results.Accepted($"/api/invoices/candidates/{id}", new { id, status = "queued" }));
        group.MapGet("/{id:guid}/attempts", (Guid id) => Results.Ok(Array.Empty<object>()));

        return app;
    }
}
```

- [ ] **Step 3: Register endpoint groups**

Modify `Program.cs` to include:

```csharp
using Pvm.Api.Features.Invoices;
using Pvm.Api.Features.Submissions;
```

And before `app.Run();`:

```csharp
app.MapInvoiceEndpoints();
app.MapSubmissionEndpoints();
```

- [ ] **Step 4: Verify API starts**

Run:

```powershell
dotnet run --project backend/src/Pvm.Api/Pvm.Api.csproj
```

Expected: API starts and `/health` returns `200`.

- [ ] **Step 5: Commit**

```powershell
git add backend/src/Pvm.Api
git commit -m "feat: add invoice workbench API surface"
```

## Task 11: Workbench Frontend Skeleton

**Files:**
- Create: `frontend/workbench/package.json`
- Create: `frontend/workbench/next.config.ts`
- Create: `frontend/workbench/tsconfig.json`
- Create: `frontend/workbench/app/layout.tsx`
- Create: `frontend/workbench/app/page.tsx`
- Create: `frontend/workbench/app/invoices/page.tsx`
- Create: `frontend/workbench/src/api/client.ts`

- [ ] **Step 1: Create Next.js app**

Run:

```powershell
New-Item -ItemType Directory -Force -Path frontend | Out-Null
npx create-next-app@latest frontend/workbench --ts --eslint --app --src-dir false --use-npm --no-tailwind
```

If the generator prompts, choose App Router and TypeScript.

- [ ] **Step 2: Add API client**

Create `frontend/workbench/src/api/client.ts`:

```ts
const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export async function getInvoiceCandidates() {
  const response = await fetch(`${apiBaseUrl}/api/invoices/candidates`, {
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load invoice candidates: ${response.status}`);
  }

  return response.json();
}
```

- [ ] **Step 3: Add invoice candidates page**

Create `frontend/workbench/app/invoices/page.tsx`:

```tsx
import { getInvoiceCandidates } from "../../src/api/client";

export default async function InvoiceCandidatesPage() {
  const candidates = await getInvoiceCandidates();

  return (
    <main>
      <h1>Invoice Submission Workbench</h1>
      <p>Invoice candidates: {Array.isArray(candidates) ? candidates.length : 0}</p>
    </main>
  );
}
```

- [ ] **Step 4: Verify frontend**

Run:

```powershell
cd frontend/workbench
npm run lint
npm run build
```

Expected: lint and build pass.

- [ ] **Step 5: Commit**

```powershell
git add frontend
git commit -m "feat: scaffold invoice workbench frontend"
```

## Task 12: End-to-End QA Submission Slice

**Files:**
- Modify: API handlers from Task 10
- Modify: repository from Task 5
- Modify: frontend invoice detail page
- Add: sanitized fixture under `backend/tests/fixtures/shoprite-invoice-basic.json`

- [ ] **Step 1: Add sanitized fixture**

Create a fixture from a representative Acumatica invoice with:

- finalized/released status
- Shoprite customer
- customer location mapping
- PO number
- supplier GLN
- DC GLN
- ZAR totals
- one normal non-catch-weight line

- [ ] **Step 2: Implement refresh command**

Implement `POST /api/invoices/refresh` so it imports the fixture first. The real Acumatica client can replace this behind the same interface when credentials and endpoint schema are available.

- [ ] **Step 3: Implement candidate detail**

`GET /api/invoices/candidates/{id}` returns:

- Acumatica invoice fields
- canonical invoice
- validation result
- generated XML
- latest submission attempts

- [ ] **Step 4: Implement submit endpoint**

`POST /api/invoices/{id}/submit` calls `SubmitShopriteInvoiceHandler` with `initiationMode = manual`.

- [ ] **Step 5: Build workbench detail page**

The page shows:

- invoice identifiers
- validation issues
- generated XML preview
- submit button only when `canSubmit = true`
- attempt history

- [ ] **Step 6: Verify local vertical slice**

Run:

```powershell
docker compose -f deploy/docker-compose.yml up -d
dotnet test backend/Pvm.sln
dotnet run --project backend/src/Pvm.Api/Pvm.Api.csproj
cd frontend/workbench
npm run build
```

Expected: tests/builds pass and UI can display fixture candidate.

- [ ] **Step 7: Commit**

```powershell
git add backend frontend
git commit -m "feat: complete invoice submission vertical slice"
```

## Task 13: QA Environment Hardening

**Files:**
- Create: `docs/runbooks/shoprite-qa-submission.md`
- Create: `deploy/azure-container-apps-notes.md`
- Modify: `README.md`

- [ ] **Step 1: Write QA submission runbook**

Create `docs/runbooks/shoprite-qa-submission.md` with:

- required Acumatica staging credentials
- required Shoprite QA credentials
- environment variables
- how to refresh candidates
- how to validate
- how to submit
- how to handle ambiguous outcomes
- where to find raw payloads and attempts

- [ ] **Step 2: Write deployment notes**

Create `deploy/azure-container-apps-notes.md` with:

- API container
- workbench container
- managed Postgres
- storage account
- secrets
- outbound network requirements
- environment variables

- [ ] **Step 3: Update README**

Add quickstart commands:

```powershell
docker compose -f deploy/docker-compose.yml up -d
dotnet test backend/Pvm.sln
dotnet run --project backend/src/Pvm.Api/Pvm.Api.csproj
cd frontend/workbench
npm run dev
```

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/runbooks deploy
git commit -m "docs: add Shoprite QA runbook"
```

## Verification Gates

Before MVP demo:

- [ ] `dotnet build backend/Pvm.sln`
- [ ] `dotnet test backend/Pvm.sln`
- [ ] `npm --prefix frontend/workbench run lint`
- [ ] `npm --prefix frontend/workbench run build`
- [ ] `git diff --check`
- [ ] API `/health` returns `200`
- [ ] Workbench displays candidate invoice
- [ ] Workbench blocks invalid invoice
- [ ] Workbench displays generated XML
- [ ] Duplicate submission is blocked
- [ ] Ambiguous Shoprite failure enters manual review state

## Open Implementation Decisions

- Confirm whether Shoprite QA accepts XML payloads for `VendorInvoice` with `Content-Type: application/xml`.
- Obtain official Shoprite XSDs.
- Confirm Acumatica endpoint entity for source invoices: `SalesInvoice`, AR `Invoice`, or both.
- Confirm where Shoprite PO number is stored in Acumatica.
- Confirm where ship-to/customer location can be read reliably in Acumatica.
- Confirm auth provider for workbench users.

## Linear Tracking

PVM Linear project seeded from this plan:

- Project: `Shoprite Invoice Upload MVP`
- Project URL: https://linear.app/pvm-backend/project/shoprite-invoice-upload-mvp-b3e83c367a09
- Team: `PVM`

Issues:

- `PVM-5` Task 1: Backend solution skeleton
- `PVM-6` Task 2: Local infrastructure and configuration
- `PVM-7` Task 3: Canonical invoice domain and validation result model
- `PVM-8` Task 4: Shoprite invoice validation rules
- `PVM-9` Task 5: Persistence schema for invoice state and audit
- `PVM-10` Task 6: Shoprite invoice XML generator
- `PVM-11` Task 7: Acumatica invoice candidate adapter
- `PVM-12` Task 8: Submission command and attempt recording
- `PVM-13` Task 9: Shoprite VendorInvoice HTTP client
- `PVM-14` Task 10: Workbench API endpoints
- `PVM-15` Task 11: Invoice Submission Workbench frontend skeleton
- `PVM-16` Task 12: End-to-end QA submission slice
- `PVM-17` Task 13: QA runbook and deployment notes

Linear access note:

- Use `LINEAR_API_KEY` from local `.env` for PVM Linear GraphQL operations.
- Do not use the MCP Linear connector for this repo until its OAuth workspace issue is fixed.
