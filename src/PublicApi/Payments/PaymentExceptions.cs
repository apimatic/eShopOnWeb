using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>The requested order/payment/card does not exist for the caller (also used for cross-owner access, so ownership is not leaked).</summary>
public sealed class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The request is malformed or violates an input rule (maps to 400).</summary>
public sealed class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The payment is not in a state that allows the requested action (maps to 409).</summary>
public sealed class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}
