using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change will cost before it is committed (UC3). All money is in whole currency
/// units, never cents.
/// </summary>
/// <remarks>
/// <see cref="Token"/> exists so a preview can never be committed silently at a different price:
/// the service re-previews at commit time and refuses when the token no longer matches, forcing
/// the customer to review a fresh quote.
/// </remarks>
public sealed record PlanChangePreview
{
    public required int SubscriptionId { get; init; }

    public required string CurrentPlanHandle { get; init; }

    public required string TargetPlanHandle { get; init; }

    public required PlanChangeTiming Timing { get; init; }

    /// <summary>The prorated credit/charge adjustment for the unused part of the current period.</summary>
    public decimal ProratedAdjustment { get; init; }

    /// <summary>The charge raised by the change.</summary>
    public decimal Charge { get; init; }

    /// <summary>The amount actually due now, after credits are applied.</summary>
    public decimal PaymentDue { get; init; }

    /// <summary>Existing credit consumed by the change.</summary>
    public decimal CreditApplied { get; init; }

    /// <summary>When the change takes effect.</summary>
    public DateTimeOffset? EffectiveAt { get; init; }

    /// <summary>
    /// A stable fingerprint of everything the customer was shown. Two previews of the same change
    /// at the same price produce the same token; any change of plan, timing, or amount does not.
    /// </summary>
    public string Token => ComputeToken();

    private string ComputeToken()
    {
        var payload = string.Join(
            '|',
            SubscriptionId.ToString(CultureInfo.InvariantCulture),
            CurrentPlanHandle,
            TargetPlanHandle,
            Timing.ToString(),
            ProratedAdjustment.ToString("F2", CultureInfo.InvariantCulture),
            Charge.ToString("F2", CultureInfo.InvariantCulture),
            PaymentDue.ToString("F2", CultureInfo.InvariantCulture),
            CreditApplied.ToString("F2", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
