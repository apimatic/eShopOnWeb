using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration amount at commit time no longer matches what was shown to the customer during
/// preview (price or proration basis changed between preview and confirm). The commit is rejected;
/// the caller must request a fresh preview (UC3 failure scenarios).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(long expectedProratedAdjustmentInCents, long actualProratedAdjustmentInCents)
        : base($"Plan change preview is stale: expected a prorated adjustment of {expectedProratedAdjustmentInCents} cents but a fresh preview now computes {actualProratedAdjustmentInCents} cents. Request a new preview before committing.")
    {
    }
}
