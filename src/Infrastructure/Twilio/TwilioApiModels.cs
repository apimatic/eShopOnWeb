using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

// These types mirror the shapes defined by the Twilio OpenAPI specification (the authoritative
// contract): the `api.v2010.account.message` schema, its list wrapper, the Lookups `LookupResponse`,
// and Twilio's standard error model. They are hand-written against api-specs/ rather than sourced
// from a third-party SDK.

/// <summary>Mirror of the OpenAPI `api.v2010.account.message` resource (fields this integration uses).</summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
}

/// <summary>Mirror of the OpenAPI `ListMessageResponse` (list + pagination cursor).</summary>
internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource> Messages { get; set; } = new();
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Mirror of the Lookups v2 `LookupResponse` (fields this integration uses).</summary>
internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}

/// <summary>Twilio's standard error model, returned on non-2xx responses.</summary>
internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
}

/// <summary>Raised when the provider returns an error. Carries the provider's code/message only — no PII.</summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? ProviderCode { get; }

    public TwilioApiException(int httpStatus, int? providerCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }
}
