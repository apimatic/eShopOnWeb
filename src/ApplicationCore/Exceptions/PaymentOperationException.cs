using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot proceed for a business reason the caller (shopper or
/// operator) can act on — e.g. paying an order that is not awaiting payment, refunding more than
/// was captured, or fulfilling an order whose authorization can no longer be renewed.
/// <see cref="SuggestedStatusCode"/> is the HTTP status the API layer should return.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message, int suggestedStatusCode = 409)
        : base(message)
    {
        SuggestedStatusCode = suggestedStatusCode;
    }

    public int SuggestedStatusCode { get; }
}
