using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

// These types mirror the request/response shapes of the Firecrawl v2 OpenAPI specification
// (firecrawl-spec/openapi.json), specifically POST /extract and GET /extract/{id}. Only the
// fields this integration uses are modeled; the spec is the authoritative contract.

/// <summary>Request body for <c>POST /extract</c>.</summary>
internal class ExtractRequest
{
    [JsonPropertyName("urls")]
    public List<string> Urls { get; set; } = new();

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>A JSON Schema object describing the structure to extract.</summary>
    [JsonPropertyName("schema")]
    public object? Schema { get; set; }

    [JsonPropertyName("enableWebSearch")]
    public bool EnableWebSearch { get; set; }

    [JsonPropertyName("includeSubdomains")]
    public bool IncludeSubdomains { get; set; }

    [JsonPropertyName("ignoreInvalidURLs")]
    public bool IgnoreInvalidURLs { get; set; } = true;
}

/// <summary>Response body for <c>POST /extract</c> (ExtractResponse in the spec).</summary>
internal class ExtractStartResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("invalidURLs")]
    public List<string>? InvalidURLs { get; set; }

    // Present on the spec's 400/500 error bodies.
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Response body for <c>GET /extract/{id}</c>.</summary>
internal class ExtractStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Object shaped by the request schema. Its exact structure is caller-defined.</summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    /// <summary>One of: completed, processing, failed, cancelled.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("tokensUsed")]
    public int? TokensUsed { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>The object we ask Firecrawl to extract: a list of products.</summary>
internal class ExtractedCatalog
{
    [JsonPropertyName("products")]
    public List<ExtractedProduct> Products { get; set; } = new();
}

/// <summary>A single product as returned by the extraction.</summary>
internal class ExtractedProduct
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // Prices arrive as displayed text (e.g. "$189.99" or "Contact for pricing"); tolerate a
    // bare number too, in case the model returns one.
    [JsonPropertyName("price")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Price { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("sku")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Sku { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

/// <summary>
/// Reads a JSON value that may be a string, number, or boolean into a string. Guards against
/// the extraction model occasionally returning a number where text is expected.
/// </summary>
internal class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out long l)
                    ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
