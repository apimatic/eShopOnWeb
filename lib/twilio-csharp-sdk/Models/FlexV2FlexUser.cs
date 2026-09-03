using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record FlexV2FlexUser
{
    /// <summary>
    /// The unique SID of the account that created the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique ID created by Twilio to identify a Flex instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^GO[0-9a-fA-F]{32}$")]
    public string? InstanceSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Twilio Unified User.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("user_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^US[0-9a-fA-F]{32}$")]
    public string? UserSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Flex User.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_user_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FU[0-9a-fA-F]{32}$")]
    public string? FlexUserSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the worker.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("worker_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WK[0-9a-fA-F]{32}$")]
    public string? WorkerSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the workspace.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Flex Team.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_team_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^QO[0-9a-fA-F]{32}$")]
    public string? FlexTeamSid { get; init; }

    /// <summary>
    /// Username of the User.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>
    /// Email of the User.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// The locale preference of the user.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    public string? Locale { get; init; }

    /// <summary>
    /// The roles of the user.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("roles")]
    public IReadOnlyList<string?>? Roles { get; init; }

    /// <summary>
    /// The date that this user was created, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_date")]
    public DateTimeOffset? CreatedDate { get; init; }

    /// <summary>
    /// The date that this user was updated, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updated_date")]
    public DateTimeOffset? UpdatedDate { get; init; }

    /// <summary>
    /// The current version of the user.
    /// </summary>
    [JsonPropertyName("version")]
    public int? Version { get; init; } = 0;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
