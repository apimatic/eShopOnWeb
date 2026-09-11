using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raised by the PayPal adapter when PayPal returns an error response. The parsed error name
/// and issue (from PayPal's standard error model in the spec) let the orchestration layer
/// react — for example, renewing a stale hold when the issue is an expired authorization.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? name, string? issue, string? description, string? debugId, string rawBody)
        : base(BuildMessage(name, issue, description, debugId))
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        Description = description;
        DebugId = debugId;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Name { get; }
    public string? Issue { get; }
    public string? Description { get; }
    public string? DebugId { get; }
    public string RawBody { get; }

    private static string BuildMessage(string? name, string? issue, string? description, string? debugId)
    {
        var core = issue ?? name ?? "PayPal request failed";
        if (!string.IsNullOrEmpty(description)) core += $": {description}";
        if (!string.IsNullOrEmpty(debugId)) core += $" (debug_id={debugId})";
        return core;
    }
}
