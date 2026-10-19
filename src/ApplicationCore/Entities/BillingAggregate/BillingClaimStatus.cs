namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum BillingClaimStatus
{
    /// <summary>The write is in flight; a second caller is refused.</summary>
    Pending = 1,

    /// <summary>The provider confirmed the write; the provider ids are recorded.</summary>
    Created = 2,

    /// <summary>The write may or may not have landed and the re-read failed; settled by a later re-read.</summary>
    Unknown = 3
}
