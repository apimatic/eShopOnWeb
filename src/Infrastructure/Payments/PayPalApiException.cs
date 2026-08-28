using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(
        HttpStatusCode statusCode,
        string name,
        string? issue,
        string? debugId,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
    public bool RequiresPayerAction =>
        Name.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
        Issue?.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) == true;
}
