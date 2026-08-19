using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

// Wire models mirroring the Firecrawl v2 OpenAPI contract for the /extract endpoints.
// Only the fields this integration consumes are modelled; unknown fields are ignored.

/// <summary>Request body for <c>POST /extract</c>.</summary>
internal sealed class ExtractRequest
{
    [JsonPropertyName("urls")]
    public string[] Urls { get; set; } = Array.Empty<string>();

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("schema")]
    public object? Schema { get; set; }
}

/// <summary>Response body for <c>POST /extract</c> (see spec schema ExtractResponse).</summary>
internal sealed class ExtractStartResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Response body for <c>GET /extract/{id}</c> (see spec schema ExtractStatusResponse).</summary>
internal sealed class ExtractStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    // While the job is still processing, Firecrawl returns `data` as an empty array; only a completed
    // job returns it as the extracted object. Kept as a raw element and interpreted by the client.
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// The <c>data</c> object of a completed extraction. Its shape is defined by the JSON schema this
/// integration sends with the request, so it is modelled directly here.
/// </summary>
internal sealed class ExtractData
{
    [JsonPropertyName("products")]
    public List<ExtractedProduct>? Products { get; set; }
}

internal sealed class ExtractedProduct
{
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    [JsonConverter(typeof(TolerantDecimalConverter))]
    public decimal? Price { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }
}

/// <summary>
/// Reads a price whether the model returns it as a JSON number or a string (e.g. "$12.49"),
/// yielding null when it is absent or cannot be parsed as a number.
/// </summary>
internal sealed class TolerantDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                // Keep only characters that can form a number (handles "$12.49", "12,49", etc. loosely).
                var cleaned = new string(Array.FindAll(raw.ToCharArray(), c => char.IsDigit(c) || c == '.' || c == '-'));
                return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
