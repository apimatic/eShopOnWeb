using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBillingEndpoints;

public class SubscriptionCreateRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
