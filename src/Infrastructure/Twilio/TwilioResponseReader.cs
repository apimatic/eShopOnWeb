using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Shared reading of Twilio JSON responses into the ApplicationCore projections, and of
/// Twilio error bodies into <see cref="TwilioApiException"/>. Kept free of any logging so
/// PII (phone numbers, message bodies) never leaks through this path.
/// </summary>
internal static class TwilioResponseReader
{
    /// <summary>Throws <see cref="TwilioApiException"/> when the response is not successful.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        int? twilioCode = null;
        string message = $"Twilio returned {(int)response.StatusCode} ({response.StatusCode}).";

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
                    twilioCode = codeEl.GetInt32();
                if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    message = msgEl.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        throw new TwilioApiException(response.StatusCode, twilioCode, message);
    }

    public static async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return MapMessage(doc.RootElement);
    }

    public static TwilioMessage MapMessage(JsonElement el) => new()
    {
        Sid = GetString(el, "sid"),
        Status = GetString(el, "status") ?? string.Empty,
        To = GetString(el, "to"),
        From = GetString(el, "from"),
        Body = GetString(el, "body"),
        ErrorCode = GetInt(el, "error_code"),
        ErrorMessage = GetString(el, "error_message"),
        MessagingServiceSid = GetString(el, "messaging_service_sid"),
        Direction = GetString(el, "direction"),
        Price = GetString(el, "price"),
        DateCreated = GetDate(el, "date_created"),
        DateSent = GetDate(el, "date_sent"),
        DateUpdated = GetDate(el, "date_updated")
    };

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement el, string name)
    {
        var s = GetString(el, name);
        if (string.IsNullOrWhiteSpace(s))
            return null;
        // Twilio timestamps are RFC 2822 (e.g. "Fri, 24 May 2019 17:44:50 +0000").
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }
}
