using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumCallDirection>))]
public sealed record CallSummariesEnumCallDirection : StringEnum<CallSummariesEnumCallDirection>
{
    private CallSummariesEnumCallDirection(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumCallDirection OutboundApi = new("outbound_api");

    public static readonly CallSummariesEnumCallDirection OutboundDial = new("outbound_dial");

    public static readonly CallSummariesEnumCallDirection Inbound = new("inbound");

    public static readonly CallSummariesEnumCallDirection TrunkingOriginating = new("trunking_originating");

    public static readonly CallSummariesEnumCallDirection TrunkingTerminating = new("trunking_terminating");

    public static CallSummariesEnumCallDirection FromValue(string value) => FromValueCore(value);
}
