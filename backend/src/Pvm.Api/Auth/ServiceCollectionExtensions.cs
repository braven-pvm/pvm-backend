using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Pvm.Api.Auth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPvmAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var authOptions = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        if (authOptions.IsDevelopmentBypass && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("AUTH_MODE=DevelopmentBypass is only allowed in Development.");
        }

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddScoped<CurrentAppUserAccessor>();

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = authOptions.IsDevelopmentBypass
                ? DevelopmentAuthenticationHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = options.DefaultAuthenticateScheme;
        });

        if (authOptions.IsDevelopmentBypass)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationHandler.SchemeName,
                _ => { });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(authOptions.TenantId))
            {
                throw new InvalidOperationException("Auth:TenantId is required when Auth:Mode is Entra.");
            }

            if (string.IsNullOrWhiteSpace(authOptions.Audience))
            {
                throw new InvalidOperationException("Auth:Audience is required when Auth:Mode is Entra.");
            }

            authentication.AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{authOptions.TenantId}/v2.0";
                options.Audience = authOptions.Audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidAudiences =
                    [
                        authOptions.Audience,
                        $"api://{authOptions.Audience}"
                    ],
                    RoleClaimType = AppAuthClaims.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Pvm.Api.Auth.JwtBearer");
                        logger.LogWarning(
                            context.Exception,
                            "JWT authentication failed for {Path}.",
                            context.HttpContext.Request.Path);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Pvm.Api.Auth.JwtBearer");
                        logger.LogWarning(
                            "JWT challenge for {Path}. Error: {Error}; Description: {ErrorDescription}",
                            context.HttpContext.Request.Path,
                            context.Error,
                            context.ErrorDescription);
                        return Task.CompletedTask;
                    }
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Invoices.Read", policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Read));
            options.AddPolicy("Invoices.Write", policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Write));
            options.AddPolicy("Admin", policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Admin));
        });

        return services;
    }
}
