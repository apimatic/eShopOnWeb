using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApiV2010AccountAuthorizedConnectApp
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the AuthorizedConnectApp resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The company name set for the Connect App.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_app_company_name")]
    public string? ConnectAppCompanyName { get; init; }

    /// <summary>
    /// A detailed description of the Connect App.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_app_description")]
    public string? ConnectAppDescription { get; init; }

    /// <summary>
    /// The name of the Connect App.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_app_friendly_name")]
    public string? ConnectAppFriendlyName { get; init; }

    /// <summary>
    /// The public URL for the Connect App.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_app_homepage_url")]
    [Format(FormatKind.Uri)]
    public string? ConnectAppHomepageUrl { get; init; }

    /// <summary>
    /// The SID that we assigned to the Connect App.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_app_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CN[0-9a-fA-F]{32}$")]
    public string? ConnectAppSid { get; init; }

    /// <summary>
    /// The set of permissions that you authorized for the Connect App.  Can be: <c>get-all</c> or <c>post-all</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("permissions")]
    public IReadOnlyList<AuthorizedConnectAppEnumPermission?>? Permissions { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
