namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum BillingClaimState
{
    /// <summary>A request owns the claim and may be calling the provider right now.</summary>
    Claimed = 1,

    /// <summary>The provider write succeeded and its id is recorded.</summary>
    Completed = 2,

    /// <summary>The write's outcome could not be settled; it may have landed.</summary>
    Unknown = 3,

    /// <summary>The provider definitively refused the write.</summary>
    Released = 4
}
