using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a plan-change commit is attempted with a staleness token that no longer matches the
/// subscription's current state (plan.md UC3: never silently apply a different amount than the one
/// previewed). The caller must request a fresh preview and re-confirm.
/// </summary>
public class PlanChangePreviewStaleException : Exception
{
    public PlanChangePreviewStaleException()
        : base("The previewed plan change is no longer current. Request a fresh preview before confirming.")
    {
    }
}
