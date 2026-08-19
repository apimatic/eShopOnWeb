using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioBillingCatalogSettings : IBillingCatalogSettings
{
    public MaxioBillingCatalogSettings(IOptions<MaxioOptions> options)
    {
        ProductFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public string ProductFamilyHandle { get; }
}
