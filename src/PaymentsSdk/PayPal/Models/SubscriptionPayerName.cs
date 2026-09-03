using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The name of the party.
/// </summary>
public record SubscriptionPayerName
{
    /// <summary>
    /// The prefix, or title, to the party's name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prefix")]
    [MaxLength(140)]
    public string? Prefix { get; init; }

    /// <summary>
    /// When the party is a person, the party's given, or first, name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("given_name")]
    [MaxLength(140)]
    public string? GivenName { get; init; }

    /// <summary>
    /// When the party is a person, the party's surname or family name. Also known as the last name. Required when the party is a person. Use also to store multiple surnames including the matronymic, or mother's, surname.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("surname")]
    [MaxLength(140)]
    public string? Surname { get; init; }

    /// <summary>
    /// When the party is a person, the party's middle name. Use also to store multiple middle names including the patronymic, or father's, middle name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("middle_name")]
    [MaxLength(140)]
    public string? MiddleName { get; init; }

    /// <summary>
    /// The suffix for the party's name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("suffix")]
    [MaxLength(140)]
    public string? Suffix { get; init; }

    /// <summary>
    /// When the party is a person, the party's full name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("full_name")]
    [MaxLength(300)]
    public string? FullName { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
