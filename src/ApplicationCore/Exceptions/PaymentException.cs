using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(
        string message,
        int statusCode,
        string? providerName = null,
        string? debugId = null,
        IReadOnlyList<string>? issues = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderName = providerName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ProviderName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
    public bool IsBrowserChallenge { get; init; }
    public bool IsUnrenewableAuthorization { get; init; }
}
