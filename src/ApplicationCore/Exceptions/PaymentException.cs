using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not proceed for a reason the caller (shopper or operator) can act on:
/// a bad state transition, a stale authorization that can no longer be renewed, an over-refund, and so on.
/// Surfaced to the API as an HTTP 4xx with the message intact.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception inner) : base(message, inner) { }
}
