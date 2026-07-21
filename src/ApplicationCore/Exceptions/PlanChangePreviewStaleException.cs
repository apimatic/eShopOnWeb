using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan-change commit is attempted with a proration preview that no longer matches a
/// freshly recomputed preview. The commit is rejected rather than silently applying a different
/// amount than the one the customer confirmed.
/// </summary>
public class PlanChangePreviewStaleException : Exception
{
    public PlanChangePreviewStaleException()
        : base("The plan change preview is stale. Request a fresh preview before confirming.")
    {
    }
}
