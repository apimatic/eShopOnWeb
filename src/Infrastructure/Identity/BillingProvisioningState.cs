namespace Microsoft.eShopWeb.Infrastructure.Identity;

public enum BillingProvisioningState
{
    Pending = 0,
    Creating = 1,
    Succeeded = 2,
    Ambiguous = 3
}
