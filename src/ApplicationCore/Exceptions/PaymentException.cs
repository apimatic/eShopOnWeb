using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A payment provider rejected or failed an operation.</summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// An authorization has gone stale and can no longer be renewed.
/// The message is phrased so an operator can act on it.
/// </summary>
public class AuthorizationNotRenewableException : PaymentException
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>The payment requires the shopper to approve it interactively (e.g. 3-D Secure), which this API does not support.</summary>
public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message) : base(message) { }
}

/// <summary>A requested entity does not exist or does not belong to the caller.</summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The request conflicts with the current state of the resource.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
