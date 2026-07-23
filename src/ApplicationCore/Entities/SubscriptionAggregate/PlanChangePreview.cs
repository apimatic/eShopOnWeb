using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to a different plan, computed before anything is committed.
/// Monetary values are decimal currency units (dollars), never minor units (cents).
/// </summary>
/// <remarks>
/// <see cref="Fingerprint"/> exists so a plan change can never charge an amount other than the one
/// the customer was shown: the commit re-computes the preview and refuses to proceed if the
/// fingerprint has moved (UC3 — "preview is stale at commit time").
/// </remarks>
public class PlanChangePreview
{
    public int SubscriptionId { get; init; }

    public string? CurrentPlanHandle { get; init; }

    public required string TargetPlanHandle { get; init; }

    public PlanChangeTiming Timing { get; init; }

    /// <summary>Net prorated adjustment for the remainder of the current period.</summary>
    public decimal ProratedAdjustment { get; init; }

    /// <summary>Gross charge produced by the change.</summary>
    public decimal Charge { get; init; }

    /// <summary>Credit applied against the charge.</summary>
    public decimal CreditApplied { get; init; }

    /// <summary>Amount actually due once credit has been applied.</summary>
    public decimal PaymentDue { get; init; }

    /// <summary>Recurring price of the target plan, per billing period.</summary>
    public decimal TargetPlanPrice { get; init; }

    /// <summary>When the change becomes effective. Null when the provider does not schedule it.</summary>
    public DateTimeOffset? EffectiveAt { get; init; }

    /// <summary>
    /// Stable digest of everything the customer was quoted. Recomputing this at commit time and
    /// comparing detects that the pricing basis moved between preview and confirm.
    /// </summary>
    public string Fingerprint => ComputeFingerprint(
        SubscriptionId, CurrentPlanHandle, TargetPlanHandle, Timing,
        ProratedAdjustment, Charge, CreditApplied, PaymentDue, TargetPlanPrice);

    /// <summary>
    /// Computes the canonical digest for a quoted plan change. Deterministic across processes:
    /// all values are formatted with the invariant culture before hashing.
    /// </summary>
    public static string ComputeFingerprint(
        int subscriptionId,
        string? currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal proratedAdjustment,
        decimal charge,
        decimal creditApplied,
        decimal paymentDue,
        decimal targetPlanPrice)
    {
        var canonical = string.Join('|',
            subscriptionId.ToString(CultureInfo.InvariantCulture),
            currentPlanHandle ?? string.Empty,
            targetPlanHandle,
            timing.ToString(),
            proratedAdjustment.ToString("F2", CultureInfo.InvariantCulture),
            charge.ToString("F2", CultureInfo.InvariantCulture),
            creditApplied.ToString("F2", CultureInfo.InvariantCulture),
            paymentDue.ToString("F2", CultureInfo.InvariantCulture),
            targetPlanPrice.ToString("F2", CultureInfo.InvariantCulture));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest);
    }
}
