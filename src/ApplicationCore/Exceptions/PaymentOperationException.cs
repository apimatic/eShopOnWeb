using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Why a payment operation could not be performed against an order in its current state.</summary>
public enum PaymentOperationError
{
    /// <summary>The order (or resource) does not exist, or does not belong to the caller.</summary>
    NotFound,

    /// <summary>The request is invalid (e.g. no payment source, or a refund amount out of range).</summary>
    Validation,

    /// <summary>The order is not in a state that allows this operation (e.g. fulfilling an unpaid order).</summary>
    Conflict
}

/// <summary>
/// A domain-level failure of a payment operation (as opposed to a provider failure, which is a
/// <see cref="PaymentGatewayException"/>). Carries a caller-safe message and a category the API
/// boundary maps to an HTTP status.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationError Error { get; }

    public PaymentOperationException(PaymentOperationError error, string message) : base(message)
    {
        Error = error;
    }
}
