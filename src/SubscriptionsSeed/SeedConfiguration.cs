using System;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The configuration the seeding tool runs from: the same <c>Maxio</c> section the hosts bind, read
/// from user-secrets and environment variables so no credential is ever written to a file in the
/// repository.
/// </summary>
public class SeedConfiguration
{
    private SeedConfiguration(MaxioSettings provider, SubscriptionSettings catalog)
    {
        Provider = provider;
        Catalog = catalog;
    }

    public MaxioSettings Provider { get; }

    public SubscriptionSettings Catalog { get; }

    public static SeedConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<SeedConfiguration>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var section = configuration.GetSection(MaxioSettings.CONFIG_SECTION);

        var provider = section.Get<MaxioSettings>() ?? new MaxioSettings();
        var catalog = section.Get<SubscriptionSettings>() ?? new SubscriptionSettings();

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException(
                "No Maxio API key is configured. Set it with:\n" +
                "  dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\" --project src/SubscriptionsSeed\n" +
                "or supply the environment variable Maxio__ApiKey.");
        }

        if (string.IsNullOrWhiteSpace(catalog.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "No product family handle is configured. Set 'Maxio:ProductFamilyHandle'.");
        }

        return new SeedConfiguration(provider, catalog);
    }
}
