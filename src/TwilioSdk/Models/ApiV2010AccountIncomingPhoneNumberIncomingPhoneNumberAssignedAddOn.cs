using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn
{
    /// <summary>
    /// The unique string that that we created to identify the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^XE[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the Phone Number to which the Add-on is assigned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resource_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? ResourceSid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// A short description of the functionality that the Add-on provides.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// A JSON string that represents the current configuration of this Add-on installation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configuration")]
    public object? Configuration { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. It can be used in place of the resource's <c>sid</c> in the URL to address the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was last updated specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// A list of related resources identified by their relative URIs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
