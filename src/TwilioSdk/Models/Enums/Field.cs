using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Field>))]
public sealed record Field : StringEnum<Field>
{
    private Field(string value) : base(value)
    {
    }

    public static readonly Field CallerName = new("caller_name");

    public static readonly Field SimSwap = new("sim_swap");

    public static readonly Field CallForwarding = new("call_forwarding");

    public static readonly Field LineTypeIntelligence = new("line_type_intelligence");

    public static readonly Field LineStatus = new("line_status");

    public static readonly Field IdentityMatch = new("identity_match");

    public static readonly Field ReassignedNumber = new("reassigned_number");

    public static readonly Field SmsPumpingRisk = new("sms_pumping_risk");

    public static Field FromValue(string value) => FromValueCore(value);
}
