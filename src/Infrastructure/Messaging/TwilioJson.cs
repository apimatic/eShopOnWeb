using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioMessageJson
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public JsonElement ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; set; }

    public ProviderMessage ToProviderMessage()
        => new(
            Sid,
            string.IsNullOrWhiteSpace(Status) ? "unknown" : Status,
            ReadErrorCode(),
            ErrorMessage,
            Body,
            From,
            NormalizeDate(DateSent),
            NormalizeDate(DateCreated),
            NormalizeDate(DateUpdated));

    private string? ReadErrorCode()
    {
        return ErrorCode.ValueKind switch
        {
            JsonValueKind.Number => ErrorCode.GetRawText(),
            JsonValueKind.String => ErrorCode.GetString(),
            _ => null
        };
    }

    private static string? NormalizeDate(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "null" ? null : value;
}

internal sealed class TwilioMessageListJson
{
    [JsonPropertyName("messages")]
    public TwilioMessageJson[] Messages { get; set; } = Array.Empty<TwilioMessageJson>();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupJson
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public string[]? ValidationErrors { get; set; }
}

internal sealed class TwilioErrorJson
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
