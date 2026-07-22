using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration a customer confirmed no longer matches what the provider would charge.
/// The plan change is refused so no amount other than the one shown is ever applied (UC3).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(string targetPlanHandle)
        : base($"The previewed cost of moving to {targetPlanHandle} is no longer current. " +
               "Request a fresh preview and confirm again.")
    {
        TargetPlanHandle = targetPlanHandle;
    }

    public string TargetPlanHandle { get; }
}
