using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of a UC3 plan change, computed before the customer commits.
/// </summary>
/// <remarks>
/// <see cref="Token"/> is a fingerprint of everything the customer was shown. The commit path
/// re-previews and compares tokens, so a preview whose basis moved between preview and confirm is
/// rejected instead of silently charging a different amount.
/// </remarks>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long creditAppliedInCents,
        long paymentDueInCents,
        long newPlanPriceInCents,
        DateTimeOffset? effectiveAt)
    {
        Guard.Against.NullOrWhiteSpace(currentPlanHandle, nameof(currentPlanHandle));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        CreditAppliedInCents = creditAppliedInCents;
        PaymentDueInCents = paymentDueInCents;
        NewPlanPriceInCents = newPlanPriceInCents;
        EffectiveAt = effectiveAt;
        Token = ComputeToken();
    }

    public int SubscriptionId { get; }

    public string CurrentPlanHandle { get; }

    public string TargetPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The net prorated adjustment for the remainder of the current period, in cents.</summary>
    public long ProratedAdjustmentInCents { get; }

    /// <summary>The gross prorated charge for the new plan, in cents.</summary>
    public long ChargeInCents { get; }

    /// <summary>The credit applied for the unused portion of the old plan, in cents.</summary>
    public long CreditAppliedInCents { get; }

    /// <summary>What the customer owes now as a result of the change, in cents.</summary>
    public long PaymentDueInCents { get; }

    /// <summary>The target plan's recurring price, in cents.</summary>
    public long NewPlanPriceInCents { get; }

    /// <summary>When the change takes effect. Null when the provider applies it immediately.</summary>
    public DateTimeOffset? EffectiveAt { get; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;

    public decimal Charge => ChargeInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal NewPlanPrice => NewPlanPriceInCents / 100m;

    /// <summary>
    /// A stable fingerprint of this preview's material facts. Two previews of the same unchanged
    /// situation produce the same token; any change to plan, timing or amounts produces a new one.
    /// </summary>
    public string Token { get; }

    private string ComputeToken()
    {
        var canonical = string.Join('|',
            SubscriptionId.ToString(CultureInfo.InvariantCulture),
            CurrentPlanHandle,
            TargetPlanHandle,
            Timing.ToString(),
            ProratedAdjustmentInCents.ToString(CultureInfo.InvariantCulture),
            ChargeInCents.ToString(CultureInfo.InvariantCulture),
            CreditAppliedInCents.ToString(CultureInfo.InvariantCulture),
            PaymentDueInCents.ToString(CultureInfo.InvariantCulture),
            NewPlanPriceInCents.ToString(CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
