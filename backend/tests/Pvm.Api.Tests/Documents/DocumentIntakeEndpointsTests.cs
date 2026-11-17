using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Api.Auth;
using Pvm.Api.Features.Documents;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Api.Tests.Documents;

public sealed class DocumentIntakeEndpointsTests : IAsyncLifetime
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a scanned proof of delivery\n%%EOF\n");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pvm-document-tests-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"documents-{Guid.NewGuid():N}";
    private WebApplication? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_NewDocument_Returns201_SavesBlob_InsertsReceivedRow_QueuesMessage()
    {
        var client = await StartAsync();
        var clientDocumentId = Guid.NewGuid();

        var response = await client.SendAsync(Upload(clientDocumentId, "pod", Pdf, pageCount: 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = body.GetProperty("documentId").GetGuid();
        Assert.Equal("Received", body.GetProperty("state").GetString());

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        var row = await db.CapturedDocuments.SingleAsync();
        Assert.Equal(documentId, row.Id);
        Assert.Equal(clientDocumentId, row.ClientDocumentId);
        Assert.Equal("POD", row.DocumentType);
        Assert.Equal(DocumentState.Received, row.State);
        Assert.Equal(2, row.PageCount);
        Assert.Equal(Pdf.LongLength, row.SizeBytes);
        Assert.Equal(64, row.Sha256.Length);
        Assert.Equal("operator@pvm.test", row.UploadedBy);
        var now = row.UploadedAt;
        Assert.Equal($"documents/POD/{now:yyyy}/{now:MM}/{documentId:D}.pdf", row.BlobPath);
        Assert.DoesNotContain("INV", row.BlobPath);

        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using (var stored = await store.OpenReadAsync(row.BlobPath, CancellationToken.None))
        using (var copy = new MemoryStream())
        {
            await stored.CopyToAsync(copy);
            Assert.Equal(Pdf, copy.ToArray());
        }

        var message = await db.IntegrationOutboxMessages.SingleAsync();
        Assert.Equal(IntegrationQueues.DocumentProcessing, message.QueueName);
        Assert.Equal("document-processing", message.QueueName);
        Assert.Equal("document.received.v1", message.MessageType);
        Assert.Equal(documentId.ToString("D"), message.CorrelationId);
        var envelope = JsonSerializer.Deserialize<IntegrationMessageEnvelope>(
            message.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(documentId, envelope.Data.GetProperty("documentId").GetGuid());
    }

    [Fact]
    public async Task Upload_SameClientDocumentIdTwice_Returns200_SameDocumentId_OneRow_OneMessage()
    {
        var client = await StartAsync();
        var clientDocumentId = Guid.NewGuid();

        var first = await client.SendAsync(Upload(clientDocumentId, "POD", Pdf));
        var second = await client.SendAsync(Upload(clientDocumentId, "POD", Pdf));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            firstBody.GetProperty("documentId").GetGuid(),
            secondBody.GetProperty("documentId").GetGuid());
        Assert.Equal("Received", secondBody.GetProperty("state").GetString());

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(1, await db.CapturedDocuments.CountAsync());
        Assert.Equal(1, await db.IntegrationOutboxMessages.CountAsync());
        Assert.Single(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_NotPdf_Returns400()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(
            Upload(Guid.NewGuid(), "POD", Encoding.ASCII.GetBytes("GIF89a not a pdf")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Upload_OverSizeLimit_Returns413()
    {
        var client = await StartAsync(maxUploadBytes: 1024);
        var large = new byte[2048];
        Pdf.CopyTo(large, 0);

        var response = await client.SendAsync(Upload(Guid.NewGuid(), "POD", large));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public void MaxUploadBytes_DefaultsTo25Megabytes_AndAttachModeToShadow()
    {
        var options = new DocumentCaptureOptions();

        Assert.Equal(25L * 1024 * 1024, options.MaxUploadBytes);
        Assert.Equal(AcumaticaAttachMode.Shadow, options.AcumaticaAttachMode);
    }

    [Fact]
    public async Task Upload_UnknownDocumentType_Returns400()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Upload(Guid.NewGuid(), "INVOICE", Pdf));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Upload_ViewerRole_Returns403()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Upload(Guid.NewGuid(), "POD", Pdf, role: AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task DocumentTypes_ReturnsTheKnownTypes()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/documents/types")
            {
                Headers = { { TestAuthHandler.RoleHeader, AppRoles.Operator } }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var types = await response.Content.ReadFromJsonAsync<JsonElement>();
        var type = Assert.Single(types.EnumerateArray());
        Assert.Equal("POD", type.GetProperty("code").GetString());
        Assert.Equal("Proof of delivery", type.GetProperty("displayName").GetString());
    }

    private async Task AssertNothingStoredAsync()
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(0, await db.CapturedDocuments.CountAsync());
        Assert.Equal(0, await db.IntegrationOutboxMessages.CountAsync());
        Assert.False(
            Directory.Exists(_root)
            && Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length > 0);
    }

    private static HttpRequestMessage Upload(
        Guid clientDocumentId,
        string documentType,
        byte[] file,
        int pageCount = 1,
        string role = AppRoles.Operator)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(file), "file", "scan.pdf" },
            { new StringContent(clientDocumentId.ToString("D")), "clientDocumentId" },
            { new StringContent(documentType), "documentType" },
            { new StringContent(pageCount.ToString()), "pageCount" }
        };
        return new HttpRequestMessage(HttpMethod.Post, "/api/documents")
        {
            Content = content,
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };
    }

    private async Task<HttpClient> StartAsync(long? maxUploadBytes = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["DocumentCapture:Provider"] = "FileSystem",
            ["DocumentCapture:FileSystemRoot"] = _root
        };
        if (maxUploadBytes is not null)
        {
            settings["DocumentCapture:MaxUploadBytes"] = maxUploadBytes.Value.ToString();
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddDbContext<PvmDbContext>(options => options
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddScoped<IIntegrationCommandQueue, IntegrationCommandQueue>();
        builder.Services.AddScoped<CurrentAppUserAccessor>();
        builder.Services.AddDocumentCapture(builder.Configuration);
        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.AddPvmPolicies);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.Use(async (context, next) =>
        {
            // Stands in for AppUserClaimsMiddleware, which resolves the user from the database.
            context.RequestServices.GetRequiredService<CurrentAppUserAccessor>().User = new CurrentAppUser(
                Guid.NewGuid(),
                null,
                "operator@pvm.test",
                null,
                context.User.FindAll(AppAuthClaims.Role).Select(claim => claim.Value).ToArray());
            await next(context);
        });
        _app.UseAuthorization();
        _app.MapDocumentIntakeEndpoints();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers[RoleHeader].FirstOrDefault();
            if (role is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, "operator@pvm.test"), new Claim(AppAuthClaims.Role, role)],
                SchemeName,
                ClaimTypes.Name,
                AppAuthClaims.Role);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
