using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Base exception for Maxio Advanced Billing API failures.</summary>
public abstract class MaxioException : Exception
{
    protected MaxioException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The requested Maxio resource (product family, product, customer, subscription) does not exist.</summary>
public sealed class MaxioNotFoundException : MaxioException
{
    public MaxioNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>Maxio rejected the request because it conflicts with existing state (e.g. duplicate subscription).</summary>
public sealed class MaxioConflictException : MaxioException
{
    public MaxioConflictException(string message) : base(message)
    {
    }
}

/// <summary>Maxio rejected the request because the input is invalid.</summary>
public sealed class MaxioValidationException : MaxioException
{
    public MaxioValidationException(string message) : base(message)
    {
    }
}

/// <summary>Maxio could not be reached or answered with an unexpected server error.</summary>
public sealed class MaxioUnavailableException : MaxioException
{
    public int? StatusCode { get; }

    public MaxioUnavailableException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Raw API-level failure: carries the HTTP status code and the error list
/// returned by Maxio so callers can decide how to surface it.
/// </summary>
public sealed class MaxioApiException : MaxioException
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string path)
        : base($"Maxio API returned {(int)statusCode} for {path}: {(errors.Count > 0 ? string.Join("; ", errors) : "no error details")}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
