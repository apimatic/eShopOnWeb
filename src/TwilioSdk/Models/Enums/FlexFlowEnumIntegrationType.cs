using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The software that will handle inbound messages. <see href="https://www.twilio.com/docs/flex/developer/messaging/manage-flows#integration-types">Integration Type</see> can be: <c>studio</c>, <c>external</c>,  or <c>task</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FlexFlowEnumIntegrationType>))]
public sealed record FlexFlowEnumIntegrationType : StringEnum<FlexFlowEnumIntegrationType>
{
    private FlexFlowEnumIntegrationType(string value) : base(value)
    {
    }

    public static readonly FlexFlowEnumIntegrationType Studio = new("studio");

    public static readonly FlexFlowEnumIntegrationType External = new("external");

    public static readonly FlexFlowEnumIntegrationType Task = new("task");

    public static FlexFlowEnumIntegrationType FromValue(string value) => FromValueCore(value);
}
