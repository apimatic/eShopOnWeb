using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failed call to the messaging provider. <see cref="Exception.Message"/> is
/// guaranteed free of shopper PII (safe for logs); <see cref="Detail"/> carries
/// the provider's own error text for storage/display and must not be logged.
/// </summary>
public class TextMessageProviderException : Exception
{
    public TextMessageProviderException(string safeMessage, string? detail = null, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Detail = detail;
    }

    public int? HttpStatusCode { get; init; }
    public int? ProviderErrorCode { get; init; }

    /// <summary>Provider error text. May reference the destination; never log this.</summary>
    public string? Detail { get; }
}
