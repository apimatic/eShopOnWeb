namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a billing claim row. A claim is written <see cref="Pending"/> before the billing provider
/// is called and moved to <see cref="Completed"/> once the provider's result is recorded. A claim that
/// stays <see cref="Pending"/> means the outcome at the provider is not (yet) known.
/// </summary>
public enum BillingClaimStatus
{
    Pending = 0,
    Completed = 1
}
