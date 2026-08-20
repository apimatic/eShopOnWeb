using System;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(
        string message,
        HttpStatusCode statusCode,
        string? debugId,
        string? issue,
        string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
    public string? ResponseBody { get; }

    public static PayPalApiException FromResponse(HttpStatusCode statusCode, string body)
    {
        string? debugId = null;
        string? issue = null;
        string? name = null;
        string? message = null;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var debug))
            {
                debugId = debug.GetString();
            }

            if (root.TryGetProperty("name", out var nameEl))
            {
                name = nameEl.GetString();
            }

            if (root.TryGetProperty("message", out var messageEl))
            {
                message = messageEl.GetString();
            }

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var issueEl))
                    {
                        issue = issueEl.GetString();
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Body is not JSON; keep the raw payload out of routine logs by not attaching it to Message.
        }

        var summary = $"PayPal request failed with {(int)statusCode}";
        if (!string.IsNullOrEmpty(name))
        {
            summary += $" {name}";
        }

        if (!string.IsNullOrEmpty(issue))
        {
            summary += $" ({issue})";
        }

        if (!string.IsNullOrEmpty(message))
        {
            summary += $": {message}";
        }

        if (!string.IsNullOrEmpty(debugId))
        {
            summary += $" [debug_id={debugId}]";
        }

        return new PayPalApiException(summary, statusCode, debugId, issue, body);
    }
}
