using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change (UC3) will cost, shown to the customer before they commit.
/// </summary>
/// <remarks>
/// <para>
/// All money is in whole currency units (dollars). <see cref="ProratedCharge"/> is what the
/// customer will be billed for the remainder of the current period on the new plan;
/// <see cref="ProratedCredit"/> is what they get back for the unused remainder of the old plan.
/// <see cref="NetAmount"/> is charge minus credit — positive means the customer pays, negative
/// means they are credited.
/// </para>
/// <para>
/// <see cref="Fingerprint"/> is the staleness guard. Re-previewing at commit time and comparing
/// fingerprints detects a changed price or proration basis; a mismatch means the customer would be
/// charged on terms they never saw, so the commit is rejected and a fresh preview is required.
/// </para>
/// </remarks>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        BillingPlan currentPlan,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        decimal proratedCharge,
        decimal proratedCredit,
        decimal amountDueNow,
        DateTimeOffset? effectiveAt)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.Null(currentPlan, nameof(currentPlan));
        Guard.Against.Null(targetPlan, nameof(targetPlan));

        SubscriptionId = subscriptionId;
        CurrentPlan = currentPlan;
        TargetPlan = targetPlan;
        Timing = timing;

        // Both figures are held as positive magnitudes; their roles carry the direction. Providers
        // sign a credit negatively, and letting that sign through would flip the net amount and
        // show a downgrade as a large charge.
        ProratedCharge = Math.Abs(proratedCharge);
        ProratedCredit = Math.Abs(proratedCredit);
        AmountDueNow = amountDueNow;
        EffectiveAt = effectiveAt;
        Fingerprint = ComputeFingerprint();
    }

    public int SubscriptionId { get; }

    public BillingPlan CurrentPlan { get; }

    public BillingPlan TargetPlan { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>Prorated charge for the new plan over the remainder of the period, in dollars.</summary>
    public decimal ProratedCharge { get; }

    /// <summary>Prorated credit for the unused remainder of the old plan, in dollars.</summary>
    public decimal ProratedCredit { get; }

    /// <summary>
    /// Charge minus credit — the economic effect of the change. Positive means the customer is out
    /// of pocket; negative means the balance moves in their favour.
    /// </summary>
    public decimal NetAmount => ProratedCharge - ProratedCredit;

    /// <summary>
    /// What the customer is actually billed at the moment they confirm, as the provider computes
    /// it. A downgrade usually nets to a credit against the account balance rather than a refund,
    /// so this is zero even though <see cref="NetAmount"/> is negative.
    /// </summary>
    public decimal AmountDueNow { get; }

    /// <summary>When the change takes effect. Null when the provider did not report a date.</summary>
    public DateTimeOffset? EffectiveAt { get; }

    /// <summary>
    /// A digest of exactly the facts the customer was shown, used to detect a stale preview
    /// at commit time.
    /// </summary>
    public string Fingerprint { get; }

    private string ComputeFingerprint()
    {
        // The fingerprint covers the *basis* of the quote — which subscription, which two plans at
        // which prices, and when the change lands — not the prorated amounts themselves.
        //
        // Proration is a function of that basis and of how much of the period remains, so the
        // amounts drift by a few cents every second. Including them would make the fingerprint
        // stale the moment it was issued and no plan change could ever be committed. Including the
        // basis still catches everything that actually matters: a repriced plan, a swapped or
        // archived target, or a different effective time would all change what the customer is
        // agreeing to, and all change the fingerprint.
        var canonical = string.Join('|',
            SubscriptionId.ToString(CultureInfo.InvariantCulture),
            CurrentPlan.Handle,
            CurrentPlan.Price.ToString("F2", CultureInfo.InvariantCulture),
            TargetPlan.Handle,
            TargetPlan.Price.ToString("F2", CultureInfo.InvariantCulture),
            Timing.ToString());

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest);
    }
}
