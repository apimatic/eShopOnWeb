using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<MetricEnumTwilioEdge>))]
public sealed record MetricEnumTwilioEdge : StringEnum<MetricEnumTwilioEdge>
{
    private MetricEnumTwilioEdge(string value) : base(value)
    {
    }

    public static readonly MetricEnumTwilioEdge UnknownEdge = new("unknown_edge");

    public static readonly MetricEnumTwilioEdge CarrierEdge = new("carrier_edge");

    public static readonly MetricEnumTwilioEdge SipEdge = new("sip_edge");

    public static readonly MetricEnumTwilioEdge SdkEdge = new("sdk_edge");

    public static readonly MetricEnumTwilioEdge ClientEdge = new("client_edge");

    public static MetricEnumTwilioEdge FromValue(string value) => FromValueCore(value);
}
