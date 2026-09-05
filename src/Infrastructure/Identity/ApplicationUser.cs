using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    // This value is scoped to the configured Maxio site. Maxio remains the source of truth;
    // the integration also looks customers up by the stable application reference.
    public int? MaxioCustomerId { get; set; }
}
