using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public CheckoutException(int statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
    public string? ProviderName { get; init; }
    public string? ProviderDebugId { get; init; }
    public IReadOnlyList<string>? Issues { get; init; }
}
