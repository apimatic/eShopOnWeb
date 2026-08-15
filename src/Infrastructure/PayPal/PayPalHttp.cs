using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Shared JSON options, the named HttpClient key, and PayPal error-model parsing.</summary>
public static class PayPalHttp
{
    /// <summary>Name of the configured HttpClient used for every PayPal call (including the token request).</summary>
    public const string ClientName = "PayPal";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Turns a PayPal error body into a <see cref="PayPalApiException"/>, preserving the error
    /// name, per-issue details and debug id from the spec's error model (and the OAuth error shape).</summary>
    internal static PayPalApiException BuildException(int statusCode, string? body)
    {
        string? name = null;
        string message = $"PayPal returned HTTP {statusCode}.";
        string? debugId = null;
        List<PayPalErrorIssue>? issues = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
                if (error is not null)
                {
                    name = error.Name ?? error.Error;
                    var describedMessage = error.Message ?? error.ErrorDescription;
                    if (!string.IsNullOrWhiteSpace(describedMessage))
                    {
                        message = describedMessage!;
                    }
                    debugId = error.DebugId;
                    if (error.Details is { Count: > 0 })
                    {
                        issues = new List<PayPalErrorIssue>();
                        foreach (var d in error.Details)
                        {
                            issues.Add(new PayPalErrorIssue(d.Issue, d.Description));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; keep the generic message but attach the raw text for diagnosis.
                message = $"PayPal returned HTTP {statusCode}: {Truncate(body, 500)}";
            }
        }

        return new PayPalApiException(statusCode, name, message, issues, debugId);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
