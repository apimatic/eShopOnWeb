using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested plan change cannot apply — for example the target plan is the one the
/// subscription is already on. Rejected before any provider call (UC3 failure scenario).
/// </summary>
public class PlanChangeNotApplicableException : Exception
{
    public PlanChangeNotApplicableException(string message) : base(message)
    {
    }
}
