using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not proceed for a business reason the caller/operator can
/// act on (e.g. an order in the wrong state, an over-refund, or an authorization that can
/// no longer be renewed). Surfaced as HTTP 409 Conflict.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}
