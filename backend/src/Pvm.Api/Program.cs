using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Pvm.Api.Auth;
using Pvm.Api.Features.Admin;
using Pvm.Api.Features.AcumaticaPushNotifications;
using Pvm.Api.Features.Auth;
using Pvm.Api.Features.Invoices;
using Pvm.Api.Features.IntegrationOperations;
using Pvm.Api.Features.Operations;
using Pvm.Api.Features.ShopritePurchaseOrders;
using Pvm.Api.Features.Submissions;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.PayloadArchive;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Shoprite;
using Pvm.Api.Features.Banking;
using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Nedbank;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("acumatica-webhook", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue(
                "AcumaticaPushNotifications:RateLimitPerMinute",
                120),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddPvmPersistence(builder.Configuration);
builder.Services.AddPayloadArchive(builder.Configuration);
builder.Services.AddPvmAuth(builder.Configuration, builder.Environment);
builder.Services.AddAcumaticaInvoiceSource(builder.Configuration);
builder.Services.AddNedbankStatementImport(builder.Configuration);
builder.Services.AddInvestecBankFeed(builder.Configuration);
builder.Services.AddShopritePurchaseOrderClient(builder.Configuration);
builder.Services.AddScoped<SubmitShopriteInvoiceHandler>();
builder.Services.AddConfiguredShopriteInvoiceClient(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<AppUserClaimsMiddleware>();
app.UseAuthorization();

app.MapInvoiceEndpoints();
app.MapBankImportEndpoints();
app.MapSubmissionEndpoints();
app.MapPurchaseOrderEndpoints();
app.MapAdminUserEndpoints();
app.MapAuthEndpoints();
app.MapIntegrationOperationEndpoints();
app.MapIntegrationRunEndpoints();
app.MapAcumaticaPushNotificationEndpoints();
app.MapAcumaticaPushNotificationAdminEndpoints();

app.Run();

public partial class Program;
