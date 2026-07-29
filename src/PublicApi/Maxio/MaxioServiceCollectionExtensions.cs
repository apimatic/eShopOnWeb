using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings binding/validation, the JSON contract, the
/// authenticated typed HTTP client, the idempotency guard, and the subscription service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate settings from the "Maxio" configuration section (values supplied via user-secrets).
        // Validation runs lazily on first use (not ValidateOnStart) so the host still boots in environments
        // without Maxio credentials — e.g. the existing functional tests, which never touch these endpoints.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_SECTION))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain) || !string.IsNullOrWhiteSpace(s.BaseUrl),
                "Maxio:Subdomain (or an explicit Maxio:BaseUrl) is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.");

        // Shared JSON contract for all Maxio (de)serialization: snake_case keys, omit nulls on write,
        // tolerate numbers-as-strings.
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        services.AddSingleton(jsonOptions);

        services.AddSingleton<MaxioIdempotencyGuard>();

        // Authenticated typed HTTP client. HTTP Basic auth uses the API key as the username and "X" as the
        // password, per Maxio's authentication docs.
        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(100);

            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
