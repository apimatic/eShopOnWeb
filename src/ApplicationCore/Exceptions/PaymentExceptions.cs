using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested entity (order, payment, saved card) does not exist. Maps to HTTP 404.</summary>
public class PaymentEntityNotFoundException : Exception
{
    public PaymentEntityNotFoundException(string message) : base(message) { }
}

/// <summary>The caller tried to see or act on data that is not theirs. Maps to HTTP 403.</summary>
public class ForbiddenPaymentAccessException : Exception
{
    public ForbiddenPaymentAccessException(string message) : base(message) { }
}

/// <summary>
/// A payment operation cannot proceed given the current state (e.g. paying an already-paid order, refunding
/// beyond the captured amount, or an authorization that can no longer be renewed). Maps to HTTP 409/422 with an
/// operator-actionable message.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message) { }
}

/// <summary>The request was malformed (e.g. no card and no saved-card id, or empty order lines). Maps to HTTP 400.</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
