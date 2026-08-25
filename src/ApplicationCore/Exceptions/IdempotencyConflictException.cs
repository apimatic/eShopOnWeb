using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a caller reuses a refund idempotency key with different request parameters
/// (e.g. a different amount) than the original request that key was used for.</summary>
public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message) : base(message)
    {
    }
}
