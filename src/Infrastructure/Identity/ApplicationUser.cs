using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    /// <summary>Advanced Billing customer identifier for this eShopOnWeb identity.</summary>
    public int? MaxioCustomerId { get; set; }

    /// <summary>Stable application-owned reference used to look up the Maxio customer.</summary>
    public string? MaxioCustomerReference { get; set; }
}
