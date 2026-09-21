using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<EventEnumTwilioEdge>))]
public sealed record EventEnumTwilioEdge : StringEnum<EventEnumTwilioEdge>
{
    private EventEnumTwilioEdge(string value) : base(value)
    {
    }

    public static readonly EventEnumTwilioEdge UnknownEdge = new("unknown_edge");

    public static readonly EventEnumTwilioEdge CarrierEdge = new("carrier_edge");

    public static readonly EventEnumTwilioEdge SipEdge = new("sip_edge");

    public static readonly EventEnumTwilioEdge SdkEdge = new("sdk_edge");

    public static readonly EventEnumTwilioEdge ClientEdge = new("client_edge");

    public static EventEnumTwilioEdge FromValue(string value) => FromValueCore(value);
}
