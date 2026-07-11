using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The commit of a plan change (UC3) could not be applied as requested: either the preview shown to the
/// customer has gone stale (the pricing basis changed between preview and confirm), or the target plan is
/// the same as the current one.
/// </summary>
public class PlanChangeException : Exception
{
    public PlanChangeException(string message) : base(message)
    {
    }
}
