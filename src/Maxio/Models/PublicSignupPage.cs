using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PublicSignupPage
{
    /// <summary>
    /// The id of the signup page (public_signup_pages only)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    /// <summary>
    /// The url to which a customer will be returned after a successful signup (public_signup_pages only).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// The params to be appended to the return_url (public_signup_pages only)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_params")]
    public string? ReturnParams { get; init; }

    /// <summary>
    /// The url where the signup page can be viewed (public_signup_pages only).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
