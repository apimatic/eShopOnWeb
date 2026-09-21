using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order/payment does not exist, or is not owned by the caller (→ 404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The payment is in a state that does not allow the requested operation (→ 409).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The request is invalid — e.g. bad amount, missing card, unknown catalog item (→ 400).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
