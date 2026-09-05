using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    /// <summary>Maxio's customer identifier for this eShopOnWeb user.</summary>
    public int? MaxioCustomerId { get; set; }
}
