using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using Microsoft.eShopWeb.PublicApi.Plaid.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public static class PlaidServiceCollectionExtensions
{
    public static IServiceCollection AddPlaidPayByBank(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.SectionName));
        services.Configure<BankPaymentSettings>(configuration.GetSection(BankPaymentSettings.SectionName));
        services.AddDataProtection();

        var inMemory = configuration["UseOnlyInMemoryDatabase"] is { } flag && bool.Parse(flag);
        if (inMemory)
        {
            services.AddDbContext<PlaidContext>(o => o.UseInMemoryDatabase("Plaid"));
        }
        else
        {
            var connection = configuration.GetConnectionString("PlaidConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:PlaidConnection is not configured.");
            services.AddDbContext<PlaidContext>(o => o.UseSqlServer(connection));
        }

        // The settings are read when the client is first needed, so the user-secrets / environment values are in place.
        services.AddThePlaidApiClient(options => Configure(options, ReadSettings(configuration)));

        services.AddSingleton<PlaidAccess>();
        services.AddSingleton<BankDirectoryService>();
        services.AddHostedService<BankDirectoryWarmup>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }

    /// <summary>The <c>Plaid:</c> section; the sandbox variables PLAID_* fill in what the section leaves empty.</summary>
    public static PlaidSettings ReadSettings(IConfiguration configuration)
    {
        var settings = configuration.GetSection(PlaidSettings.SectionName).Get<PlaidSettings>() ?? new PlaidSettings();
        settings.ClientId = FirstNonEmpty(settings.ClientId, configuration["PLAID_CLIENT_ID"]);
        settings.Secret = FirstNonEmpty(settings.Secret, configuration["PLAID_SECRET"]);
        settings.Environment = FirstNonEmpty(settings.Environment, configuration["PLAID_ENV"]);
        return settings;
    }

    private static string? FirstNonEmpty(string? a, string? b) => string.IsNullOrWhiteSpace(a) ? (string.IsNullOrWhiteSpace(b) ? null : b) : a;

    private static void Configure(ThePlaidApiClientOptions options, PlaidSettings settings)
    {
        options.ClientId = settings.ClientId;
        options.Secret = settings.Secret;
        options.PlaidVersion = settings.Version;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // An explicit address wins over the environment and is used exactly as given.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl;
            options.Environment = ServerEnvironment.Environment2;
            return;
        }

        options.Environment = settings.Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => ServerEnvironment.Environment2,
            "production" => ServerEnvironment.Production,
            null or "" => throw new BankApiException(503, "Plaid:Environment is not configured (sandbox or production)."),
            var other => throw new BankApiException(503, $"Plaid:Environment '{other}' is not supported (sandbox or production)."),
        };
    }
}
