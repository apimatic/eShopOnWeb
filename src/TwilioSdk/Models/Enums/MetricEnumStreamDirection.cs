using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<MetricEnumStreamDirection>))]
public sealed record MetricEnumStreamDirection : StringEnum<MetricEnumStreamDirection>
{
    private MetricEnumStreamDirection(string value) : base(value)
    {
    }

    public static readonly MetricEnumStreamDirection Unknown = new("unknown");

    public static readonly MetricEnumStreamDirection Inbound = new("inbound");

    public static readonly MetricEnumStreamDirection Outbound = new("outbound");

    public static readonly MetricEnumStreamDirection Both = new("both");

    public static MetricEnumStreamDirection FromValue(string value) => FromValueCore(value);
}
