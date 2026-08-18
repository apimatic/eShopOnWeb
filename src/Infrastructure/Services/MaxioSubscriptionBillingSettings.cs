using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionBillingSettings : ISubscriptionBillingSettings
{
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingSettings(IOptions<MaxioOptions> options)
    {
        _options = options.Value;
    }

    public string ProductFamilyHandle => _options.ProductFamilyHandle ?? string.Empty;
}
