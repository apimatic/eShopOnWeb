using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record InteractExecuteResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

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
