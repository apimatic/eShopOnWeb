using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected a call. Carries the error model fields from the PayPal OpenAPI specs
/// (name, message, debug_id and the fine-grained issue code).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int httpStatusCode, string? name, string? issue, string? debugId, string message)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorName = name;
        Issue = issue;
        DebugId = debugId;
    }

    public int HttpStatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
