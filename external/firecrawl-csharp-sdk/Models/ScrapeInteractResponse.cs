using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ScrapeInteractResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>
    /// Raw Chrome DevTools Protocol (CDP) WebSocket URL for the browser session. Use it to connect directly with Playwright, Puppeteer, or any CDP client.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cdpUrl")]
    public string? CdpUrl { get; init; }

    /// <summary>
    /// Read-only live view URL for the browser session
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("liveViewUrl")]
    public string? LiveViewUrl { get; init; }

    /// <summary>
    /// Interactive live view URL (viewers can control the browser)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interactiveLiveViewUrl")]
    public string? InteractiveLiveViewUrl { get; init; }

    /// <summary>
    /// AI agent's final response (only present when using prompt)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>
    /// Standard output from the code execution
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stdout")]
    public string? Stdout { get; init; }

    /// <summary>
    /// Standard output (alias for stdout)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("result")]
    public string? Result { get; init; }

    /// <summary>
    /// Standard error output from the code execution
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stderr")]
    public string? Stderr { get; init; }

    /// <summary>
    /// Exit code of the executed process
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    /// <summary>
    /// Whether the process was killed due to timeout
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("killed")]
    public bool? Killed { get; init; }

    /// <summary>
    /// Error message if the code raised an exception
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
