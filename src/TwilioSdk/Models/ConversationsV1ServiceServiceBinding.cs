using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ConversationsV1ServiceServiceBinding
{
    /// <summary>
    /// A 34 character string that uniquely identifies this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BS[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this binding.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Binding resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/conversations/api/credential-resource">Credential</see> for the binding. See <see href="https://www.twilio.com/docs/chat/push-notification-configuration">push notification configuration</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credential_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CR[0-9a-fA-F]{32}$")]
    public string? CredentialSid { get; init; }

    /// <summary>
    /// The date that this resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The unique endpoint identifier for the Binding. The format of this value depends on the <c>binding_type</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>
    /// The application-defined string that uniquely identifies the <see href="https://www.twilio.com/docs/conversations/api/user-resource">Conversation User</see> within the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see>. See <see href="https://www.twilio.com/docs/conversations/create-tokens">access tokens</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity")]
    public string? Identity { get; init; }

    /// <summary>
    /// The push technology to use for the Binding. Can be: <c>apn</c>, <c>gcm</c>, <c>fcm</c>, or <c>twilsock</c>.  See <see href="https://www.twilio.com/docs/chat/push-notification-configuration">push notification configuration</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("binding_type")]
    public ServiceBindingEnumBindingType? BindingType { get; init; }

    /// <summary>
    /// The <see href="https://www.twilio.com/docs/chat/push-notification-configuration#push-types">Conversation message types</see> the binding is subscribed to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_types")]
    public IReadOnlyList<string?>? MessageTypes { get; init; }

    /// <summary>
    /// An absolute API resource URL for this binding.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
