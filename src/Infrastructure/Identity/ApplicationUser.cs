using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    // The Advanced Billing customer is owned by Maxio; this is only a durable link to it.
    public long? MaxioCustomerId { get; set; }
}
