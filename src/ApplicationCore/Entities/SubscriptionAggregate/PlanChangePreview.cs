using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A previewed plan change (UC3): the prorated amounts the provider would charge/credit if committed
/// "now", or the effective date of a plan change scheduled for the next renewal (no proration).
/// <see cref="PreviewToken"/> is an opaque, tamper-evident, time-limited token that <c>ISubscriptionService</c>
/// requires back on commit so that the amount actually applied can never silently drift from the amount
/// shown to the customer (plan §UC3 failure scenarios).
/// </summary>
public record PlanChangePreview(
    int SubscriptionId,
    string FromProductHandle,
    string ToProductHandle,
    bool ApplyNow,
    long ProratedAdjustmentInCents,
    long ChargeInCents,
    long PaymentDueInCents,
    long CreditAppliedInCents,
    DateTimeOffset EffectiveAt,
    string PreviewToken);
