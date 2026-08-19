using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Firecrawl;

/// <summary>
/// Request for Firecrawl's <c>POST /extract</c> endpoint: pull structured data out of one or
/// more pages using an LLM guided by a JSON Schema.
/// </summary>
public sealed class FirecrawlExtractRequest
{
    /// <summary>URLs (glob format supported) to extract data from.</summary>
    public required IReadOnlyList<string> Urls { get; init; }

    /// <summary>Natural-language prompt guiding the extraction.</summary>
    public string? Prompt { get; init; }

    /// <summary>JSON Schema (as a serializable object) describing the shape to extract.</summary>
    public object? Schema { get; init; }
}

/// <summary>Result of starting an extract job (<c>POST /extract</c>).</summary>
public sealed class FirecrawlExtractJob
{
    public bool Success { get; init; }
    public string? Id { get; init; }

    /// <summary>Any URLs from the request that were rejected as invalid and skipped.</summary>
    public IReadOnlyList<string>? InvalidUrls { get; init; }
}

/// <summary>Status values reported by Firecrawl for an extract job.</summary>
public enum FirecrawlJobStatus
{
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Status and result of an extract job (<c>GET /extract/{id}</c>).</summary>
public sealed class FirecrawlExtractResult
{
    public bool Success { get; init; }
    public FirecrawlJobStatus Status { get; init; }

    /// <summary>The extracted data object, shaped by the request schema. Null until completed.</summary>
    public JsonElement? Data { get; init; }

    public int? TokensUsed { get; init; }
}
