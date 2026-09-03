using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record FlexV1Configuration
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Configuration resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The date and time in GMT when the Configuration resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the Configuration resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// An object that contains application-specific data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public object? Attributes { get; init; }

    /// <summary>
    /// The status of the Flex onboarding. Can be: <c>ok</c>, <c>inprogress</c>,<c>notstarted</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public ConfigurationEnumStatus? Status { get; init; }

    /// <summary>
    /// The SID of the TaskRouter Workspace.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? TaskrouterWorkspaceSid { get; init; }

    /// <summary>
    /// The SID of the TaskRouter target Workflow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_target_workflow_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WW[0-9a-fA-F]{32}$")]
    public string? TaskrouterTargetWorkflowSid { get; init; }

    /// <summary>
    /// The SID of the TaskRouter Target TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_target_taskqueue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? TaskrouterTargetTaskqueueSid { get; init; }

    /// <summary>
    /// The list of TaskRouter TaskQueues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_taskqueues")]
    public IReadOnlyList<object?>? TaskrouterTaskqueues { get; init; }

    /// <summary>
    /// The Skill description for TaskRouter workers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_skills")]
    public IReadOnlyList<object?>? TaskrouterSkills { get; init; }

    /// <summary>
    /// The TaskRouter default channel capacities and availability for workers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_worker_channels")]
    public object? TaskrouterWorkerChannels { get; init; }

    /// <summary>
    /// The TaskRouter Worker attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_worker_attributes")]
    public object? TaskrouterWorkerAttributes { get; init; }

    /// <summary>
    /// The TaskRouter SID of the offline activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taskrouter_offline_activity_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WA[0-9a-fA-F]{32}$")]
    public string? TaskrouterOfflineActivitySid { get; init; }

    /// <summary>
    /// The URL where the Flex instance is hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("runtime_domain")]
    public string? RuntimeDomain { get; init; }

    /// <summary>
    /// The SID of the Messaging service instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messaging_service_instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? MessagingServiceInstanceSid { get; init; }

    /// <summary>
    /// The SID of the chat service this user belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceInstanceSid { get; init; }

    /// <summary>
    /// The SID of the Flex service instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_service_instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? FlexServiceInstanceSid { get; init; }

    /// <summary>
    /// The SID of the Flex instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^GO[0-9a-fA-F]{32}$")]
    public string? FlexInstanceSid { get; init; }

    /// <summary>
    /// The primary language of the Flex UI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ui_language")]
    public string? UiLanguage { get; init; }

    /// <summary>
    /// The object that describes Flex UI characteristics and settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ui_attributes")]
    public object? UiAttributes { get; init; }

    /// <summary>
    /// The object that defines the NPM packages and versions to be used in Hosted Flex.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ui_dependencies")]
    public object? UiDependencies { get; init; }

    /// <summary>
    /// The Pinned UI version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ui_version")]
    public string? UiVersion { get; init; }

    /// <summary>
    /// The Flex Service version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_version")]
    public string? ServiceVersion { get; init; }

    /// <summary>
    /// Whether call recording is enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_recording_enabled")]
    public bool? CallRecordingEnabled { get; init; }

    /// <summary>
    /// The call recording webhook URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_recording_webhook_url")]
    public string? CallRecordingWebhookUrl { get; init; }

    /// <summary>
    /// Whether CRM is present for Flex.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crm_enabled")]
    public bool? CrmEnabled { get; init; }

    /// <summary>
    /// The CRM type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crm_type")]
    public string? CrmType { get; init; }

    /// <summary>
    /// The CRM Callback URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crm_callback_url")]
    public string? CrmCallbackUrl { get; init; }

    /// <summary>
    /// The CRM Fallback URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crm_fallback_url")]
    public string? CrmFallbackUrl { get; init; }

    /// <summary>
    /// An object that contains the CRM attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crm_attributes")]
    public object? CrmAttributes { get; init; }

    /// <summary>
    /// The list of public attributes, which are visible to unauthenticated clients.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("public_attributes")]
    public object? PublicAttributes { get; init; }

    /// <summary>
    /// Whether the plugin service enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_service_enabled")]
    public bool? PluginServiceEnabled { get; init; }

    /// <summary>
    /// The plugin service attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_service_attributes")]
    public object? PluginServiceAttributes { get; init; }

    /// <summary>
    /// A list of objects that contain the configurations for the Integrations supported in this configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("integrations")]
    public IReadOnlyList<object?>? Integrations { get; init; }

    /// <summary>
    /// The list of outbound call flows.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_call_flows")]
    public object? OutboundCallFlows { get; init; }

    /// <summary>
    /// The list of serverless service SIDs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("serverless_service_sids")]
    public IReadOnlyList<string?>? ServerlessServiceSids { get; init; }

    /// <summary>
    /// Configurable parameters for Queues Statistics.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("queue_stats_configuration")]
    public object? QueueStatsConfiguration { get; init; }

    /// <summary>
    /// Configurable parameters for Notifications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notifications")]
    public object? Notifications { get; init; }

    /// <summary>
    /// Configurable parameters for Markdown.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("markdown")]
    public object? Markdown { get; init; }

    /// <summary>
    /// The absolute URL of the Configuration resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Object with enabled/disabled flag with list of workspaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_insights_hr")]
    public object? FlexInsightsHr { get; init; }

    /// <summary>
    /// Setting this to true will redirect Flex UI to the URL set in flex_url
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_insights_drilldown")]
    public bool? FlexInsightsDrilldown { get; init; }

    /// <summary>
    /// URL to redirect to in case drilldown is enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_url")]
    public string? FlexUrl { get; init; }

    /// <summary>
    /// Settings for different limits for Flex Conversations channels attachments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_configs")]
    public IReadOnlyList<object?>? ChannelConfigs { get; init; }

    /// <summary>
    /// Configurable parameters for Debugger Integration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("debugger_integration")]
    public object? DebuggerIntegration { get; init; }

    /// <summary>
    /// Configurable parameters for Flex UI Status report.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_ui_status_report")]
    public object? FlexUiStatusReport { get; init; }

    /// <summary>
    /// Agent conversation end methods.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("agent_conv_end_methods")]
    public object? AgentConvEndMethods { get; init; }

    /// <summary>
    /// Citrix voice vdi configuration and settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("citrix_voice_vdi")]
    public object? CitrixVoiceVdi { get; init; }

    /// <summary>
    /// Presence and presence ttl configuration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offline_config")]
    public object? OfflineConfig { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
