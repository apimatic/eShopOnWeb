using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a failure from the PayPal gateway. <see cref="IsProviderRejection"/> distinguishes a
/// deliberate rejection by PayPal (bad input, over-refund, expired/non-reauthorizable
/// authorization - caller-actionable) from a transport/unknown failure (provider unreachable,
/// unexpected response shape - not caller-actionable).
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, bool isProviderRejection, string? debugId = null, IReadOnlyList<string>? issues = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsProviderRejection = isProviderRejection;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    public bool IsProviderRejection { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}
