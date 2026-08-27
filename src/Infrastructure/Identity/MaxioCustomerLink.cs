using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public BillingProvisioningState State { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ApplicationUser? User { get; set; }
}
