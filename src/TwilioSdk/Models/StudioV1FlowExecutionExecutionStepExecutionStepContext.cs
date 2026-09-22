using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record StudioV1FlowExecutionExecutionStepExecutionStepContext
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the ExecutionStepContext resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The current state of the Flow's Execution. As a flow executes, we save its state in this context. We save data that your widgets can access as variables in configuration fields or in text areas as variable substitution.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("context")]
    public object? Context { get; init; }

    /// <summary>
    /// The SID of the context's Execution resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("execution_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FN[0-9a-fA-F]{32}$")]
    public string? ExecutionSid { get; init; }

    /// <summary>
    /// The SID of the Flow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flow_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FW[0-9a-fA-F]{32}$")]
    public string? FlowSid { get; init; }

    /// <summary>
    /// The SID of the Step that the context is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("step_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FT[0-9a-fA-F]{32}$")]
    public string? StepSid { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
