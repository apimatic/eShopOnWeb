using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration basis moved between preview and commit, so the customer would have been charged
/// an amount other than the one they confirmed. The commit is rejected and a fresh preview required.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(decimal confirmedAmount, decimal currentAmount)
        : base($"The confirmed plan-change amount of {confirmedAmount} no longer matches the current amount of {currentAmount}. Request a fresh preview before committing.")
    {
        ConfirmedAmount = confirmedAmount;
        CurrentAmount = currentAmount;
    }

    public decimal ConfirmedAmount { get; }
    public decimal CurrentAmount { get; }
}
