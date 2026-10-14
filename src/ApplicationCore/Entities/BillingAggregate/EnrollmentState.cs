namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum EnrollmentState
{
    /// <summary>Claimed; the request that claimed it is creating the subscription right now.</summary>
    Pending,

    /// <summary>The create call failed in a way that leaves its outcome unknown; must be settled by re-reading the billing system.</summary>
    Unknown,

    /// <summary>The subscription exists in the billing system.</summary>
    Active
}
