using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the billing provider failed. This is the single typed exception the provider seam
/// raises, so callers never have to know which SDK or transport sits behind
/// <see cref="Interfaces.IBillingClient"/>.
/// </summary>
public class BillingProviderException : Exception
{
    private static readonly IReadOnlyList<string> NoMessages = Array.Empty<string>();

    public BillingProviderException(string message)
        : this(message, statusCode: null, providerMessages: null, innerException: null)
    {
    }

    public BillingProviderException(string message, Exception? innerException)
        : this(message, statusCode: null, providerMessages: null, innerException: innerException)
    {
    }

    public BillingProviderException(string message,
        int? statusCode,
        IEnumerable<string>? providerMessages,
        Exception? innerException = null)
        : base(BuildMessage(message, providerMessages), innerException)
    {
        StatusCode = statusCode;
        ProviderMessages = providerMessages?.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray() ?? NoMessages;
    }

    /// <summary>The HTTP status the provider responded with, when the failure reached it.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error messages, when it returned any.</summary>
    public IReadOnlyList<string> ProviderMessages { get; }

    private static string BuildMessage(string message, IEnumerable<string>? providerMessages)
    {
        var details = providerMessages?.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
        return details is { Length: > 0 }
            ? $"{message} ({string.Join("; ", details)})"
            : message;
    }
}
