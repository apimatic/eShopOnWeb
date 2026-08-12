using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A Twilio Message resource (<c>api.v2010.account.message</c> in the OpenAPI spec),
/// carrying only the fields this integration reads.
/// </summary>
public class TwilioMessage
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    [JsonConverter(typeof(FlexibleNullableIntConverter))]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSentRaw { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreatedRaw { get; set; }

    [JsonPropertyName("date_updated")]
    public string? DateUpdatedRaw { get; set; }

    public DateTimeOffset? DateSent => ParseRfc2822(DateSentRaw);
    public DateTimeOffset? DateCreated => ParseRfc2822(DateCreatedRaw);

    internal static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed;

        return null;
    }
}

/// <summary>The list envelope returned by <c>ListMessage</c>.</summary>
public class TwilioMessageListPage
{
    [JsonPropertyName("messages")]
    public List<TwilioMessage> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

/// <summary>
/// Tolerates Twilio returning <c>error_code</c> as a JSON number, a numeric string, or null.
/// </summary>
public class FlexibleNullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetInt32();
            case JsonTokenType.String:
                var s = reader.GetString();
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
