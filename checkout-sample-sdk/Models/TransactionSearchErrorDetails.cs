using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The error details. Required for client-side <c>4XX</c> errors.
/// </summary>
public record TransactionSearchErrorDetails
{
    /// <summary>
    /// The field that caused the error. If this field is in the body, set this value to the field's JSON pointer value. Required for client-side errors.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    /// <summary>
    /// The value of the field that caused the error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>
    /// The location of the field that caused the error. Value is <c>body</c>, <c>path</c>, or <c>query</c>.
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; init; } = "body";

    /// <summary>
    /// The unique, fine-grained application-level error code.
    /// </summary>
    [JsonPropertyName("issue")]
    public required string Issue { get; init; }

    /// <summary>
    /// The human-readable description for an issue. The description can change over the lifetime of an API, so clients must not depend on this value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
