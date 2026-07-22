using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The prorated cost of moving a subscription to a different plan, computed by the billing provider
/// before anything is committed. All amounts are in whole currency units (dollars), never cents.
/// <para>
/// <see cref="Signature"/> is a deterministic fingerprint of everything the customer was shown. The
/// commit step re-runs the preview and compares signatures, so a preview whose basis moved between
/// display and confirmation is rejected instead of silently charging a different amount
/// (UC3 failure scenario: "never silently apply a different amount than the one shown").
/// </para>
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal proratedAdjustment,
        decimal charge,
        decimal paymentDue,
        decimal creditApplied,
        decimal targetPlanPrice)
    {
        Guard.Against.NullOrEmpty(currentPlanHandle, nameof(currentPlanHandle));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustment = proratedAdjustment;
        Charge = charge;
        PaymentDue = paymentDue;
        CreditApplied = creditApplied;
        TargetPlanPrice = targetPlanPrice;
    }

    public int SubscriptionId { get; }

    public string CurrentPlanHandle { get; }

    public string TargetPlanHandle { get; }

    public string? CurrentPlanName { get; init; }

    public string? TargetPlanName { get; init; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The prorated adjustment for the remainder of the current period. Negative means a credit.</summary>
    public decimal ProratedAdjustment { get; }

    /// <summary>The amount that would be charged for the change.</summary>
    public decimal Charge { get; }

    /// <summary>The amount actually due now, after any credit is applied.</summary>
    public decimal PaymentDue { get; }

    public decimal CreditApplied { get; }

    /// <summary>The recurring price of the target plan, in whole currency units.</summary>
    public decimal TargetPlanPrice { get; }

    /// <summary>When the change would take effect. Null for an immediate change.</summary>
    public DateTimeOffset? EffectiveAt { get; init; }

    /// <summary>
    /// A stable fingerprint of the priced facts shown to the customer. Two previews match only when
    /// the subscription, both plans, the timing and every amount are identical.
    /// </summary>
    public string Signature
    {
        get
        {
            var canonical = string.Join('|',
                SubscriptionId.ToString(CultureInfo.InvariantCulture),
                CurrentPlanHandle,
                TargetPlanHandle,
                Timing.ToString(),
                ProratedAdjustment.ToString("F2", CultureInfo.InvariantCulture),
                Charge.ToString("F2", CultureInfo.InvariantCulture),
                PaymentDue.ToString("F2", CultureInfo.InvariantCulture),
                CreditApplied.ToString("F2", CultureInfo.InvariantCulture),
                TargetPlanPrice.ToString("F2", CultureInfo.InvariantCulture));

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(hash);
        }
    }

    /// <summary>Whether this preview prices the same change, on the same basis, as <paramref name="other"/>.</summary>
    public bool Matches(PlanChangePreview? other) =>
        other is not null && string.Equals(Signature, other.Signature, StringComparison.Ordinal);
}
