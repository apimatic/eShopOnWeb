using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Loads the seeder's configuration from the same sources the application uses.
/// </summary>
/// <remarks>
/// Nothing is read from a file in the repository: the API key and site come from .NET user-secrets or
/// from environment variables. The bare <c>MAXIO_*</c> variables are accepted as a convenience so an
/// operator can run the seeder straight from a provisioned shell, but user-secrets always win.
/// </remarks>
internal static class SeedConfiguration
{
    public static MaxioSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(FromBareEnvironmentVariables())
            .AddUserSecrets(typeof(SeedConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>()
            ?? new MaxioSettings();

        ApplyDemoDefaults(settings);
        settings.Validate();

        return settings;
    }

    /// <summary>Maps the provisioning environment's bare variable names onto the typed settings keys.</summary>
    private static IEnumerable<KeyValuePair<string, string?>> FromBareEnvironmentVariables()
    {
        var mappings = new Dictionary<string, string>
        {
            ["MAXIO_API_KEY"] = $"{MaxioSettings.SectionName}:ApiKey",
            ["MAXIO_SITE_SUBDOMAIN"] = $"{MaxioSettings.SectionName}:Subdomain",
            ["MAXIO_ENVIRONMENT"] = $"{MaxioSettings.SectionName}:Environment",
            ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = $"{MaxioSettings.SectionName}:ProductFamilyHandle"
        };

        foreach (var (variable, key) in mappings)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string?>(key, value);
            }
        }
    }

    /// <summary>
    /// Fills in the demo catalogue's handles when they were not configured, so a fresh sandbox can be
    /// seeded with nothing but an API key and a site.
    /// </summary>
    private static void ApplyDemoDefaults(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            settings.ProductFamilyHandle = "eshop-subscribe";
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultProductHandle))
        {
            settings.DefaultProductHandle = "eshop-pro";
        }

        if (string.IsNullOrWhiteSpace(settings.AlternateProductHandle))
        {
            settings.AlternateProductHandle = "basic-plan";
        }

        if (string.IsNullOrWhiteSpace(settings.MeteredComponentHandle))
        {
            settings.MeteredComponentHandle = "api-call";
        }
    }
}
