using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? payPalDebugId = null,
        string? payPalIssue = null) : base(message)
    {
        StatusCode = statusCode;
        PayPalDebugId = payPalDebugId;
        PayPalIssue = payPalIssue;
    }

    public int StatusCode { get; }
    public string? PayPalDebugId { get; }
    public string? PayPalIssue { get; }
}
