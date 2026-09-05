using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public int? MaxioCustomerId { get; set; }
    public string? MaxioCustomerReference { get; set; }
}
