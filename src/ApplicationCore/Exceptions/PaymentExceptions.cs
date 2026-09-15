using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment provider was unreachable, returned a server error, or returned a response that could
/// not be processed. The outcome is unknown; surfaces to the caller as a 502. The message is always
/// a curated, caller-safe string — never a raw provider or serializer message.
/// </summary>
public class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The payment provider deliberately rejected the request (a 4xx). The caller/operator can act on
/// the reason. Surfaces as a 422. The message is a curated, caller-safe string.
/// </summary>
public class PaymentRejectedException : Exception
{
    public PaymentRejectedException(string message) : base(message)
    {
    }
}

/// <summary>
/// A control-flow signal: the authorization has expired and must be renewed (re-authorized) before
/// it can be captured. Not surfaced to callers directly — the orchestrator handles it.
/// </summary>
public class PaymentAuthorizationExpiredException : Exception
{
    public PaymentAuthorizationExpiredException(string message) : base(message)
    {
    }
}

/// <summary>An order/payment operation was requested in a state that does not allow it. Surfaces as a 409.</summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}

/// <summary>
/// A requested entity does not exist, or is not owned by the caller. The two are deliberately
/// indistinguishable so one shopper cannot probe another's data. Surfaces as a 404.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
