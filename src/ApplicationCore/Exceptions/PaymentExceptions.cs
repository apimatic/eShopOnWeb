using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The order/payment/saved-card does not exist, or does not belong to the caller (404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The requested operation is not valid for the payment's current state (409).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The caller's input was invalid (400).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
