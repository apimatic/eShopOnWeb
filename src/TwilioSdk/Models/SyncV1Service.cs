using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record SyncV1Service
{
    /// <summary>
    /// The unique string that we created to identify the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. It can be used in place of the resource's <c>sid</c> in the URL to address the resource. It is a read-only property, it cannot be assigned using REST API.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URL we call when Sync objects are manipulated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook_url")]
    [Format(FormatKind.Uri)]
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Whether the Service instance should call <c>webhook_url</c> when the REST API is used to update Sync objects. The default is <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhooks_from_rest_enabled")]
    public bool? WebhooksFromRestEnabled { get; init; }

    /// <summary>
    /// Whether the service instance calls <c>webhook_url</c> when client endpoints connect to Sync. The default is <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reachability_webhooks_enabled")]
    public bool? ReachabilityWebhooksEnabled { get; init; }

    /// <summary>
    /// Whether token identities in the Service must be granted access to Sync objects by using the <see href="https://www.twilio.com/docs/sync/api/sync-permissions">Permissions</see> resource. It is disabled (false) by default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("acl_enabled")]
    public bool? AclEnabled { get; init; }

    /// <summary>
    /// Whether every <c>endpoint_disconnected</c> event should occur after a configurable delay. The default is <c>false</c>, where the <c>endpoint_disconnected</c> event occurs immediately after disconnection. When <c>true</c>, intervening reconnections can prevent the <c>endpoint_disconnected</c> event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reachability_debouncing_enabled")]
    public bool? ReachabilityDebouncingEnabled { get; init; }

    /// <summary>
    /// The reachability event delay in milliseconds if <c>reachability_debouncing_enabled</c> = <c>true</c>.  Must be between 1,000 and 30,000 and defaults to 5,000. This is the number of milliseconds after the last running client disconnects, and a Sync identity is declared offline, before <c>webhook_url</c> is called, if all endpoints remain offline. A reconnection from the same identity by any endpoint during this interval prevents the reachability event from occurring.
    /// </summary>
    [JsonPropertyName("reachability_debouncing_window")]
    public int? ReachabilityDebouncingWindow { get; init; } = 0;

    /// <summary>
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
