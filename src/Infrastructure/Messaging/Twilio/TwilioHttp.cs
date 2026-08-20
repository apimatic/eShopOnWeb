using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

internal static class TwilioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static AuthenticationHeaderValue BasicAuth(string accountSid, string authToken)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? twilioCode = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed))
            {
                twilioCode = parsed;
            }
        }
        catch (JsonException)
        {
            // The provider error body is not logged: it may contain a destination number.
        }

        throw new TwilioApiException((int)response.StatusCode, twilioCode);
    }
}

internal sealed class TwilioErrorBody
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}

internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }
}

internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource>? Messages { get; set; }

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}
