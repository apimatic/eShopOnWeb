using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The request was well-formed but invalid for a payment operation (maps to HTTP 400).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The order/card was not found or is not the caller's (maps to HTTP 404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the order's current payment state (maps to HTTP 409).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}
