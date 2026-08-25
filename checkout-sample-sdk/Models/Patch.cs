using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The JSON patch object to apply partial updates to resources.
/// </summary>
public record Patch
{
    /// <summary>
    /// The operation.
    /// </summary>
    [JsonPropertyName("op")]
    public required PatchOp Op { get; init; }

    /// <summary>
    /// The JSON Pointer to the target document location at which to complete the operation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// The value to apply. The remove, copy, and move operations do not require a value. Since JSON Patch allows any type for value, the type property is not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("value")]
    public object? Value { get; init; }

    /// <summary>
    /// The JSON Pointer to the target document location from which to move the value. Required for the move operation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    public string? From { get; init; }
}
