using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : AuthenticatedRequest
{
    /// <summary>The number as the shopper typed it. It is validated and canonicalised by the provider.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
