using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the proration figures confirmed by the customer no longer match a freshly
/// computed preview at commit time (UC3: never silently apply a different amount than the one shown).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException()
        : base("The previewed plan-change cost is no longer current. Request a fresh preview before confirming.")
    {
    }
}
