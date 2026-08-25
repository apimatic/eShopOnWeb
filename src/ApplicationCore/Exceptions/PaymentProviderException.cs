using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by an <see cref="Interfaces.IPaymentProvider"/> implementation when the provider rejects
/// a request or the request cannot be completed. Carries an operator-actionable message and never
/// the raw provider exception message (which may contain sensitive detail).
/// </summary>
public class PaymentProviderException : Exception
{
    public PaymentProviderException(string message) : base(message)
    {
    }

    public PaymentProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the payment provider indicates the shopper must complete an interactive approval
/// step (e.g. a 3DS/contingency challenge) before the payment can proceed. This integration does not
/// build a browser approval round-trip, so this exception is surfaced as an operator-actionable error.
/// </summary>
public class PaymentApprovalRequiredException : PaymentProviderException
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when an authorization has gone stale (past its honor period) and the provider reports it
/// can no longer be renewed/reauthorized.
/// </summary>
public class PaymentAuthorizationNotRenewableException : PaymentProviderException
{
    public PaymentAuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
