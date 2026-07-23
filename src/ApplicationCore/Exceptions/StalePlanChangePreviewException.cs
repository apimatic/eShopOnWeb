using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration preview the customer confirmed no longer matches what the provider would charge, or it
/// is older than the accepted validity window. The commit is refused so the customer is never charged an
/// amount other than the one they were shown (plan.md UC3, "preview is stale at commit time").
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(string message) : base(message)
    {
    }

    public static StalePlanChangePreviewException Expired() =>
        new("The proration preview has expired. Refresh the preview and confirm the amount again.");

    public static StalePlanChangePreviewException Missing() =>
        new("An immediate plan change must confirm a proration preview before it can be applied.");

    public static StalePlanChangePreviewException AmountChanged(long confirmedInCents, long currentInCents) =>
        new($"The prorated amount changed from {confirmedInCents / 100m:N2} to {currentInCents / 100m:N2} " +
            "since the preview was shown. Refresh the preview and confirm the new amount.");
}
