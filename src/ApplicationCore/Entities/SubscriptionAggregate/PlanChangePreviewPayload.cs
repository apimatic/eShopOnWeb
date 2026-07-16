using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The data sealed inside a <see cref="PlanChangePreview.PreviewToken"/>. Carried round-trip from preview
/// to commit so the commit step can detect a stale preview (plan §UC3) without a second network round trip.
/// </summary>
public record PlanChangePreviewPayload(
    int SubscriptionId,
    string CustomerReference,
    string FromProductHandle,
    string ToProductHandle,
    bool ApplyNow,
    long ProratedAdjustmentInCents,
    long ChargeInCents,
    long PaymentDueInCents,
    long CreditAppliedInCents,
    DateTimeOffset ExpiresAtUtc);
