namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a committed plan change takes effect. UC3 describes two timings ("apply now with
/// proration" or "at next renewal without proration"), but the Maxio Advanced Billing .NET SDK's
/// subscription-migration operation has no field that defers a commit to the next renewal — every
/// combination of its PreservePeriod/Proration flags bills immediately (confirmed against SDK
/// source). Only <see cref="Immediate"/> is therefore implemented; requesting
/// <see cref="AtNextRenewal"/> is rejected with <see cref="Exceptions.PlanChangeNotSupportedException"/>
/// rather than approximated.
/// </summary>
public enum PlanChangeTiming
{
    Immediate,
    AtNextRenewal
}
