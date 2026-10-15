namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum EnrollmentStatus
{
    /// <summary>Claimed; the subscription has not been confirmed by the billing system yet.</summary>
    Pending = 0,

    /// <summary>The billing system holds the subscription.</summary>
    Active = 1,

    /// <summary>The create call failed in a way that leaves its outcome unknown; must be settled by reference.</summary>
    Unknown = 2
}
