using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan change cannot be performed: the target plan is the same as the current one
/// (no-op, rejected before any provider call), or the requested timing is not a capability the
/// Maxio Advanced Billing .NET SDK exposes. Deferring a plan change to the next renewal without
/// proration has no corresponding SDK operation (confirmed against SDK source — the migration
/// models' PreservePeriod/Proration flags only ever bill immediately); rather than inventing a
/// workaround, that timing is rejected here and surfaced explicitly to the caller.
/// </summary>
public class PlanChangeNotSupportedException : Exception
{
    public PlanChangeNotSupportedException(string message) : base(message)
    {
    }
}
