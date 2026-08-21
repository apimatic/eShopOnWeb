using System;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message, string? debugId, string? issue)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public string? Issue { get; }

    public static PayPalApiException FromResponse(HttpStatusCode statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? statusCode.ToString() : statusCode.ToString();
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "PayPal request failed." : "PayPal request failed.";
            var debugId = root.TryGetProperty("debug_id", out var debugEl) ? debugEl.GetString() : null;
            string? issue = null;
            string? description = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = detail.TryGetProperty("issue", out var issueEl) ? issueEl.GetString() : issue;
                    description = detail.TryGetProperty("description", out var descEl) ? descEl.GetString() : description;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                message = $"{message} {description}";
            }

            if (!string.IsNullOrWhiteSpace(debugId))
            {
                message = $"{message} (PayPal debug_id {debugId})";
            }

            return new PayPalApiException(statusCode, name, message.Trim(), debugId, issue);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, statusCode.ToString(), "PayPal request failed.", null, null);
        }
    }
}
