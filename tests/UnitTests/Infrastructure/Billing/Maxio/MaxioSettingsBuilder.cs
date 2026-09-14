using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

internal sealed class MaxioSettingsBuilder
{
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe",
        CatalogCacheDuration = TimeSpan.Zero,
        MaxRetryAttempts = 0,
    };

    public MaxioSettingsBuilder WithDefaultPlan(string handle)
    {
        _settings.DefaultPlanHandle = handle;
        return this;
    }

    public MaxioSettingsBuilder WithRetries(int attempts)
    {
        _settings.MaxRetryAttempts = attempts;
        _settings.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
        return this;
    }

    public MaxioSettings Build() => _settings;
}
