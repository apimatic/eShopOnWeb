using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Turns a PayPal error response body into a <see cref="PayPalApiException"/> carrying its issue codes.</summary>
internal static class PayPalErrorParser
{
    public static PayPalApiException ToException(int statusCode, string? body)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var issues = new List<string>();

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var error = JsonSerializer.Deserialize<ErrorDto>(body, PayPalJson.Options);
                name = error?.Name;
                message = error?.Message;
                debugId = error?.DebugId;
                if (error?.Details is not null)
                {
                    foreach (var detail in error.Details)
                    {
                        if (!string.IsNullOrEmpty(detail.Issue))
                        {
                            var text = detail.Issue!;
                            if (!string.IsNullOrEmpty(detail.Field))
                            {
                                text += $" [field: {detail.Field}]";
                            }
                            if (!string.IsNullOrEmpty(detail.Description))
                            {
                                text += $" ({detail.Description})";
                            }
                            issues.Add(text);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON body — keep the raw text as the message below.
            }

            // The OAuth token endpoint uses a different shape: { error, error_description }.
            if (name is null && message is null)
            {
                try
                {
                    var oauth = JsonSerializer.Deserialize<OAuthError>(body, PayPalJson.Options);
                    name = oauth?.Error;
                    message = oauth?.ErrorDescription;
                }
                catch (JsonException)
                {
                }
            }

            message ??= body.Length > 500 ? body.Substring(0, 500) : body;
        }

        message ??= "PayPal request failed.";
        return new PayPalApiException(statusCode, name, message, issues, debugId);
    }

    private sealed class OAuthError
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }
}
