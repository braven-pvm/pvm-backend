using System.Text.Json.Serialization;
using Pvm.Api.Auth;
using Pvm.Api.Features.Admin;
using Pvm.Api.Features.Auth;
using Pvm.Api.Features.Invoices;
using Pvm.Api.Features.Submissions;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddPvmPersistence(builder.Configuration);
builder.Services.AddPvmAuth(builder.Configuration, builder.Environment);
builder.Services.AddScoped<SubmitShopriteInvoiceHandler>();
builder.Services.AddSingleton<IShopriteInvoiceClient, LocalShopriteInvoiceClient>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.UseAuthentication();
app.UseMiddleware<AppUserClaimsMiddleware>();
app.UseAuthorization();

app.MapInvoiceEndpoints();
app.MapSubmissionEndpoints();
app.MapAdminUserEndpoints();
app.MapAuthEndpoints();

app.Run();

public partial class Program;
