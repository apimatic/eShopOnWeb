using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl.Models;

/// <summary>
/// Request body for <c>POST /extract</c> (spec operation <c>extractData</c>). Only the fields this
/// integration uses are modelled; all are defined in the Firecrawl OpenAPI spec.
/// </summary>
public class FirecrawlExtractRequest
{
    /// <summary>The URLs to extract data from (spec: required).</summary>
    [JsonPropertyName("urls")]
    public IReadOnlyList<string> Urls { get; set; } = new List<string>();

    /// <summary>Prompt to guide the extraction process (spec: optional).</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>JSON Schema describing the structure of the extracted data (spec: optional object).</summary>
    [JsonPropertyName("schema")]
    public object? Schema { get; set; }
}

/// <summary>
/// Response body for <c>POST /extract</c> (spec schema <c>ExtractResponse</c>). The job runs
/// asynchronously; <see cref="Id"/> identifies it for status polling.
/// </summary>
public class FirecrawlExtractResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("invalidURLs")]
    public IReadOnlyList<string>? InvalidURLs { get; set; }
}

/// <summary>
/// Response body for <c>GET /extract/{id}</c> (spec schema <c>ExtractStatusResponse</c>). The
/// extracted structured data is carried in <see cref="Data"/>, shaped by the request schema.
/// </summary>
public class FirecrawlExtractStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The extracted data, shaped by the schema sent with the request.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>One of: completed, processing, failed, cancelled (spec enum).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("tokensUsed")]
    public int? TokensUsed { get; set; }
}

/// <summary>The spec's error model returned on 4xx/5xx: <c>{ "error": string }</c>.</summary>
public class FirecrawlErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
