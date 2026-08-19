using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingFailureKind
{
    ClientError,
    NotFound,
    ProviderError,
    Unreachable,
    UnreadableSuccess
}

/// <summary>
/// Caller-safe failure from the billing provider. <see cref="Exception.Message"/> is already sanitized.
/// </summary>
public sealed class BillingException : Exception
{
    public BillingException(string message, int statusCode, BillingFailureKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Kind = kind;
    }

    public int StatusCode { get; }
    public BillingFailureKind Kind { get; }
}
