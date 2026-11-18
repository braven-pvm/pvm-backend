using System.Net;
using System.Net.Http.Headers;
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
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Tests.Documents;

/// <summary>
/// Device enrolment and the device endpoints (design 6.10). The one-code-enrols-once race runs on
/// Postgres in ScannerDeviceServiceTests; EF InMemory has no real concurrency.
/// </summary>
public sealed class ScannerDeviceEndpointsTests : IAsyncLifetime
{
    private const string ApiBase = "https://ca-pvm-api-qa.example.test";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a scanned proof of delivery\n%%EOF\n");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pvm-device-tests-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"devices-{Guid.NewGuid():N}";
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
    public async Task CreateDevice_Returns201_WithSetupCodeAndQrPayload_StoresOnlyTheHash()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = " Dispatch phone 1 " }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var device = body.GetProperty("device");
        Assert.Equal("Dispatch phone 1", device.GetProperty("name").GetString());
        Assert.Equal("Pending", device.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, device.GetProperty("enrolledAt").ValueKind);
        var code = body.GetProperty("setupCode").GetString()!;
        Assert.Equal(43, code.Length);
        Assert.DoesNotContain('=', code);
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));

        using var qr = JsonDocument.Parse(body.GetProperty("qrPayload").GetString()!);
        Assert.Equal(1, qr.RootElement.GetProperty("v").GetInt32());
        Assert.Equal(ApiBase, qr.RootElement.GetProperty("api").GetString());
        Assert.Equal(code, qr.RootElement.GetProperty("code").GetString());

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        var stored = await db.ScannerDeviceSetupCodes.SingleAsync();
        Assert.Equal(ScannerDeviceService.Hash(code), stored.CodeHash);
        Assert.Equal("admin@pvm.test", stored.CreatedBy);
        Assert.Equal("admin@pvm.test", (await db.ScannerDevices.SingleAsync()).CreatedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://ca-pvm-api-qa.example.test")]
    [InlineData("not a url")]
    public async Task CreateDevice_BaseUrlNotConfigured_Returns500_CreatesNothing(string baseUrl)
    {
        var client = await StartAsync(deviceApiBaseUrl: baseUrl);

        var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = "Dispatch phone 1" }));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Scanner setup is not configured: set DocumentCapture:DeviceApiBaseUrl",
            problem.GetProperty("detail").GetString());
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(0, await db.ScannerDevices.CountAsync());
        Assert.Equal(0, await db.ScannerDeviceSetupCodes.CountAsync());
    }

    [Fact]
    public async Task NewSetupCode_BaseUrlNotConfigured_Returns500_CreatesNoCode()
    {
        var client = await StartAsync(deviceApiBaseUrl: "");
        var deviceId = await SeedDeviceAsync("Dispatch phone 1");

        var response = await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{deviceId:D}/setup-code"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(0, await db.ScannerDeviceSetupCodes.CountAsync());
    }

    [Fact]
    public async Task CreateDevice_NameMissingTooLongOrTaken_Returns400Or409()
    {
        var client = await StartAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = "  " }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = new string('x', 101) }))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = "Dispatch phone 1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name = "DISPATCH PHONE 1" }))).StatusCode);
    }

    [Fact]
    public async Task Enrol_ValidCode_Returns200_WithKeyAndName_AndListShowsActive()
    {
        var client = await StartAsync();
        var (deviceId, code) = await CreateDeviceAsync(client, "Dispatch phone 1");

        var response = await client.PostAsJsonAsync("/api/device/enrol", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var key = body.GetProperty("deviceKey").GetString()!;
        Assert.StartsWith("pvmd_", key);
        Assert.Equal(48, key.Length);
        Assert.Equal("Dispatch phone 1", body.GetProperty("deviceName").GetString());

        var list = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/scanner-devices")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal(deviceId, item.GetProperty("id").GetGuid());
        Assert.Equal("Active", item.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("enrolledAt").ValueKind);

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(ScannerDeviceService.Hash(key), (await db.ScannerDevices.SingleAsync()).KeyHash);
    }

    [Fact]
    public async Task Enrol_ExpiredUsedUnknownOrRevokedCode_Returns400_WithTheSameBody()
    {
        var client = await StartAsync();

        var (_, used) = await CreateDeviceAsync(client, "Used");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/device/enrol", new { code = used })).StatusCode);

        var (expiredDevice, expired) = await CreateDeviceAsync(client, "Expired");
        await using (var scope = _app!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
            var row = await db.ScannerDeviceSetupCodes.SingleAsync(item => item.DeviceId == expiredDevice);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var (revokedDevice, revoked) = await CreateDeviceAsync(client, "Revoked");
        Assert.Equal(HttpStatusCode.OK,
            (await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{revokedDevice:D}/revoke"))).StatusCode);

        var bodies = new List<string>();
        foreach (var code in new[] { used, expired, "unknown-code", revoked })
        {
            var response = await client.PostAsJsonAsync("/api/device/enrol", new { code });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        var empty = await client.PostAsJsonAsync("/api/device/enrol", new { code = "" });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        bodies.Add(await empty.Content.ReadAsStringAsync());

        Assert.Single(bodies.Distinct());
        Assert.Contains("setup code is not valid", bodies[0]);
    }

    [Fact]
    public async Task Enrol_EleventhCallInAMinute_Returns429()
    {
        var client = await StartAsync();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/device/enrol", new { code = $"wrong-{i}" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var eleventh = await client.PostAsJsonAsync("/api/device/enrol", new { code = "wrong-11" });
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    [Fact]
    public async Task DeviceConfig_WithKey_ReturnsNameTypesAndActiveStaff_AndUpdatesLastSeen()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");
        var thabo = await CreateStaffAsync(client, "Thabo");
        var anna = await CreateStaffAsync(client, "Anna");
        var gone = await CreateStaffAsync(client, "Gone");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(
            Admin(HttpMethod.Put, $"/api/scanner-staff/{gone:D}", new { name = "Gone", active = false }))).StatusCode);

        var response = await client.SendAsync(Device(HttpMethod.Get, "/api/device/config", key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dispatch phone 1", body.GetProperty("deviceName").GetString());
        var type = Assert.Single(body.GetProperty("documentTypes").EnumerateArray());
        Assert.Equal("POD", type.GetProperty("code").GetString());
        Assert.Equal("Proof of delivery", type.GetProperty("displayName").GetString());
        var staff = body.GetProperty("staff").EnumerateArray().ToList();
        Assert.Equal([anna, thabo], staff.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(["Anna", "Thabo"], staff.Select(item => item.GetProperty("name").GetString()));

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.NotNull((await db.ScannerDevices.SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task DeviceKey_IsRejectedOnAWorkbenchEndpoint()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");

        var search = await client.SendAsync(Device(HttpMethod.Get, "/api/documents", key));
        var devices = await client.SendAsync(Device(HttpMethod.Get, "/api/scanner-devices", key));
        var upload = await client.SendAsync(DeviceUpload("/api/documents", key, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, search.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, devices.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    [Fact]
    public async Task WorkbenchUser_IsRejectedOnTheDeviceEndpoints()
    {
        var client = await StartAsync();

        var config = await client.SendAsync(Admin(HttpMethod.Get, "/api/device/config"));
        var upload = DeviceUpload("/api/device/documents", key: null, Guid.NewGuid());
        upload.Headers.Add(TestAuthHandler.RoleHeader, AppRoles.Admin);
        var uploaded = await client.SendAsync(upload);

        Assert.Equal(HttpStatusCode.Unauthorized, config.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, uploaded.StatusCode);
    }

    [Fact]
    public async Task DevelopmentBypassUser_IsRejectedOnTheDeviceEndpoints()
    {
        var client = await StartAsync(developmentBypass: true);

        var config = await client.GetAsync("/api/device/config");
        var bogus = await client.SendAsync(Device(HttpMethod.Get, "/api/device/config", "pvmd_not-a-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, config.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task RevokedDevice_Gets401_AndRevokeClearsTheKey()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");
        var deviceId = await SingleDeviceIdAsync();

        var revoke = await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{deviceId:D}/revoke"));

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var body = await revoke.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Revoked", body.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Device(HttpMethod.Get, "/api/device/config", key))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(DeviceUpload("/api/device/documents", key, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{deviceId:D}/setup-code"))).StatusCode);

        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        var device = await db.ScannerDevices.SingleAsync();
        Assert.Null(device.KeyHash);
        Assert.Equal("admin@pvm.test", device.RevokedBy);
        Assert.NotNull(device.RevokedAt);
    }

    [Fact]
    public async Task NewSetupCode_InvalidatesTheOldUnusedCode_AndReEnrolmentReplacesTheKey()
    {
        var client = await StartAsync();
        var (deviceId, firstCode) = await CreateDeviceAsync(client, "Dispatch phone 1");
        var firstKey = await EnrolAsync(client, firstCode);

        var second = await NewCodeAsync(client, deviceId);
        var third = await NewCodeAsync(client, deviceId);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/device/enrol", new { code = second })).StatusCode);
        var newKey = await EnrolAsync(client, third);
        Assert.NotEqual(firstKey, newKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Device(HttpMethod.Get, "/api/device/config", firstKey))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Device(HttpMethod.Get, "/api/device/config", newKey))).StatusCode);
    }

    [Fact]
    public async Task NewSetupCode_UnknownDevice_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{Guid.NewGuid():D}/setup-code"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeviceUpload_StoresTheDocument_WithDeviceAndStaff_AndIsIdempotent()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");
        var thabo = await CreateStaffAsync(client, "Thabo");
        var clientDocumentId = Guid.NewGuid();

        var first = await client.SendAsync(DeviceUpload("/api/device/documents", key, clientDocumentId, staffId: thabo.ToString("D")));
        var second = await client.SendAsync(DeviceUpload("/api/device/documents", key, clientDocumentId, staffId: thabo.ToString("D")));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = firstBody.GetProperty("documentId").GetGuid();
        Assert.Equal(documentId, secondBody.GetProperty("documentId").GetGuid());
        Assert.Equal("Received", firstBody.GetProperty("state").GetString());

        await using (var scope = _app!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
            var row = await db.CapturedDocuments.SingleAsync();
            var device = await db.ScannerDevices.SingleAsync();
            Assert.Equal("device:Dispatch phone 1", row.UploadedBy);
            Assert.Equal(device.Id, row.ScannerDeviceId);
            Assert.Equal(thabo, row.ScannerStaffId);
            Assert.Equal("Thabo", row.ScannerStaffName);
            Assert.Equal(DocumentState.Received, row.State);
            Assert.Equal(1, await db.IntegrationOutboxMessages.CountAsync());
        }

        // The workbench sees who scanned it: search, detail and the review list.
        var search = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/documents"))).Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(search.GetProperty("items").EnumerateArray());
        Assert.Equal("Dispatch phone 1", item.GetProperty("scannerDeviceName").GetString());
        Assert.Equal("Thabo", item.GetProperty("scannerStaffName").GetString());

        var detail = await (await client.SendAsync(Admin(HttpMethod.Get, $"/api/documents/{documentId:D}"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dispatch phone 1", detail.GetProperty("scannerDeviceName").GetString());
        Assert.Equal("Thabo", detail.GetProperty("scannerStaffName").GetString());

        await using (var scope = _app!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
            var row = await db.CapturedDocuments.SingleAsync();
            row.State = DocumentState.NeedsReview;
            await db.SaveChangesAsync();
        }

        var review = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/documents/review"))).Content.ReadFromJsonAsync<JsonElement>();
        var reviewItem = Assert.Single(review.GetProperty("items").EnumerateArray());
        Assert.Equal("Dispatch phone 1", reviewItem.GetProperty("scannerDeviceName").GetString());
        Assert.Equal("Thabo", reviewItem.GetProperty("scannerStaffName").GetString());
    }

    [Fact]
    public async Task DeviceUpload_WithoutStaff_LeavesStaffEmpty_AndWorkbenchUploadsShowNoScanner()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");

        var response = await client.SendAsync(DeviceUpload("/api/device/documents", key, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        var row = await db.CapturedDocuments.SingleAsync();
        Assert.Null(row.ScannerStaffId);
        Assert.Null(row.ScannerStaffName);
        Assert.NotNull(row.ScannerDeviceId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    public async Task DeviceUpload_StaffIdNotAnActiveStaffName_Returns400_StoresNothing(string kind)
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");
        var staffId = kind switch
        {
            "not-a-guid" => "thabo",
            "unknown" => Guid.NewGuid().ToString("D"),
            _ => await InactiveStaffAsync(client)
        };

        var response = await client.SendAsync(DeviceUpload("/api/device/documents", key, Guid.NewGuid(), staffId: staffId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        Assert.Equal(0, await db.CapturedDocuments.CountAsync());
        Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task DeviceUpload_NotPdf_Returns400()
    {
        var client = await StartAsync();
        var key = await EnrolDeviceAsync(client, "Dispatch phone 1");

        var response = await client.SendAsync(
            DeviceUpload("/api/device/documents", key, Guid.NewGuid(), file: Encoding.ASCII.GetBytes("GIF89a")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Staff_CreateRenameTurnOff_AndList()
    {
        var client = await StartAsync();

        var created = await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-staff", new { name = " Thabo " }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var staff = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = staff.GetProperty("id").GetGuid();
        Assert.Equal("Thabo", staff.GetProperty("name").GetString());
        Assert.True(staff.GetProperty("active").GetBoolean());

        var updated = await client.SendAsync(Admin(HttpMethod.Put, $"/api/scanner-staff/{id:D}", new { name = "Thabo M", active = false }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Thabo M", updatedBody.GetProperty("name").GetString());
        Assert.False(updatedBody.GetProperty("active").GetBoolean());

        var list = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/scanner-staff"))).Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("Thabo M", item.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-staff", new { name = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Admin(HttpMethod.Put, $"/api/scanner-staff/{id:D}", new { name = new string('x', 101), active = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.SendAsync(Admin(HttpMethod.Put, $"/api/scanner-staff/{Guid.NewGuid():D}", new { name = "X", active = true }))).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/scanner-devices")]
    [InlineData("POST", "/api/scanner-devices")]
    [InlineData("POST", "/api/scanner-devices/00000000-0000-0000-0000-000000000001/setup-code")]
    [InlineData("POST", "/api/scanner-devices/00000000-0000-0000-0000-000000000001/revoke")]
    [InlineData("GET", "/api/scanner-staff")]
    [InlineData("POST", "/api/scanner-staff")]
    [InlineData("PUT", "/api/scanner-staff/00000000-0000-0000-0000-000000000001")]
    public async Task AdminEndpoints_OperatorGets403(string method, string path)
    {
        var client = await StartAsync();

        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Headers = { { TestAuthHandler.RoleHeader, AppRoles.Operator } }
        };
        if (method != "GET")
        {
            request.Content = JsonContent.Create(new { name = "X", active = true });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> InactiveStaffAsync(HttpClient client)
    {
        var id = await CreateStaffAsync(client, "Gone");
        await client.SendAsync(Admin(HttpMethod.Put, $"/api/scanner-staff/{id:D}", new { name = "Gone", active = false }));
        return id.ToString("D");
    }

    private async Task<Guid> SeedDeviceAsync(string name)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        var device = new ScannerDeviceEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedBy = "admin@pvm.test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ScannerDevices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }

    private async Task<Guid> SingleDeviceIdAsync()
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        return (await db.ScannerDevices.SingleAsync()).Id;
    }

    private static async Task<(Guid DeviceId, string Code)> CreateDeviceAsync(HttpClient client, string name)
    {
        var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-devices", new { name }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("device").GetProperty("id").GetGuid(), body.GetProperty("setupCode").GetString()!);
    }

    private static async Task<string> NewCodeAsync(HttpClient client, Guid deviceId)
    {
        var response = await client.SendAsync(Admin(HttpMethod.Post, $"/api/scanner-devices/{deviceId:D}/setup-code"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(deviceId, body.GetProperty("device").GetProperty("id").GetGuid());
        return body.GetProperty("setupCode").GetString()!;
    }

    private static async Task<string> EnrolAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/device/enrol", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deviceKey").GetString()!;
    }

    private static async Task<string> EnrolDeviceAsync(HttpClient client, string name)
    {
        var (_, code) = await CreateDeviceAsync(client, name);
        return await EnrolAsync(client, code);
    }

    private static async Task<Guid> CreateStaffAsync(HttpClient client, string name)
    {
        var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/scanner-staff", new { name }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static HttpRequestMessage Admin(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { { TestAuthHandler.RoleHeader, AppRoles.Admin } }
        };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage Device(HttpMethod method, string path, string key)
        => new(method, path) { Headers = { Authorization = new AuthenticationHeaderValue("Device", key) } };

    private static HttpRequestMessage DeviceUpload(
        string path,
        string? key,
        Guid clientDocumentId,
        string? staffId = null,
        byte[]? file = null)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(file ?? Pdf), "file", "scan.pdf" },
            { new StringContent(clientDocumentId.ToString("D")), "clientDocumentId" },
            { new StringContent("POD"), "documentType" },
            { new StringContent("1"), "pageCount" }
        };
        if (staffId is not null)
        {
            content.Add(new StringContent(staffId), "staffId");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Device", key);
        }

        return request;
    }

    private async Task<HttpClient> StartAsync(string? deviceApiBaseUrl = ApiBase, bool developmentBypass = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentCapture:Provider"] = "FileSystem",
            ["DocumentCapture:FileSystemRoot"] = _root,
            ["DocumentCapture:DeviceApiBaseUrl"] = deviceApiBaseUrl
        });
        builder.Services.AddDbContext<PvmDbContext>(options => options
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddScoped<IIntegrationCommandQueue, IntegrationCommandQueue>();
        builder.Services.AddScoped<CurrentAppUserAccessor>();
        builder.Services.AddDocumentCapture(builder.Configuration);
        builder.Services.AddScoped<DocumentIntakeService>();
        builder.Services.AddScoped<ScannerDeviceService>();
        builder.Services.AddScoped<DocumentReviewService>();
        builder.Services.AddScoped<DocumentProcessor>();
        builder.Services.AddSingleton<IAcumaticaDocumentClient, UnusedAcumatica>();
        builder.Services.AddRateLimiter(options => options.AddDeviceEnrolRateLimit());

        var defaultScheme = developmentBypass ? DevelopmentAuthenticationHandler.SchemeName : TestAuthHandler.SchemeName;
        var authentication = builder.Services.AddAuthentication(defaultScheme);
        if (developmentBypass)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationHandler.SchemeName, _ => { });
        }
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        }

        authentication.AddDeviceKey();
        builder.Services.AddAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.AddPvmPolicies);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseRateLimiter();
        _app.Use(async (context, next) =>
        {
            // Stands in for AppUserClaimsMiddleware, which resolves the user from the database.
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var roles = context.User.FindAll(AppAuthClaims.Role).Select(claim => claim.Value).ToArray();
                if (developmentBypass)
                {
                    roles = [AppRoles.Admin];
                    foreach (var role in roles)
                    {
                        ((ClaimsIdentity)context.User.Identity).AddClaim(new Claim(AppAuthClaims.Role, role));
                        ((ClaimsIdentity)context.User.Identity).AddClaim(new Claim(ClaimTypes.Role, role));
                    }
                }

                context.RequestServices.GetRequiredService<CurrentAppUserAccessor>().User = new CurrentAppUser(
                    Guid.NewGuid(), null, "admin@pvm.test", null, roles);
            }

            await next(context);
        });
        _app.UseAuthorization();
        _app.MapDocumentIntakeEndpoints();
        _app.MapDocumentReviewEndpoints();
        _app.MapScannerAdminEndpoints();
        _app.MapDeviceEndpoints();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private sealed class UnusedAcumatica : IAcumaticaDocumentClient
    {
        public Task<InvoiceCandidate?> GetInvoiceAsync(string referenceNumber, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AttachFileAsync(string entity, string referenceNumber, string fileName, Stream pdf, CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
                [new Claim(ClaimTypes.Email, "admin@pvm.test"), new Claim(AppAuthClaims.Role, role)],
                SchemeName,
                ClaimTypes.Name,
                AppAuthClaims.Role);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
