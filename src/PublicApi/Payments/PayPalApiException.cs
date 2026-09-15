using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string code, string message,
        string? debugId, string? issue = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        DebugId = debugId;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
    public bool RequiresPayerAction =>
        Code.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase) ||
        (Issue?.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase) ?? false) ||
        Code.Contains("3D_SECURE", StringComparison.OrdinalIgnoreCase) ||
        (Issue?.Contains("3D_SECURE", StringComparison.OrdinalIgnoreCase) ?? false);
}
