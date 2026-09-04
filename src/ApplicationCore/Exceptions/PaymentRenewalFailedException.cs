using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment authorization has gone stale and PayPal can no longer renew it, so the
/// money can no longer be taken against it. The operator must re-collect payment.
/// </summary>
public class PaymentRenewalFailedException : Exception
{
    public string PayPalIssue { get; }

    public PaymentRenewalFailedException(string message, string payPalIssue = "") : base(message)
    {
        PayPalIssue = payPalIssue;
    }
}