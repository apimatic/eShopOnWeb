namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum BillingClaimState
{
    /// <summary>Claimed; the provider call is in flight.</summary>
    Pending = 0,

    /// <summary>The provider confirmed the result and its id is recorded.</summary>
    Completed = 1,

    /// <summary>The provider call failed in a way that leaves its outcome unknown; it must be settled by re-reading the provider.</summary>
    Unknown = 2
}
