using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SyncMapItemEnumQueryFromBoundType>))]
public sealed record SyncMapItemEnumQueryFromBoundType : StringEnum<SyncMapItemEnumQueryFromBoundType>
{
    private SyncMapItemEnumQueryFromBoundType(string value) : base(value)
    {
    }

    public static readonly SyncMapItemEnumQueryFromBoundType Inclusive = new("inclusive");

    public static readonly SyncMapItemEnumQueryFromBoundType Exclusive = new("exclusive");

    public static SyncMapItemEnumQueryFromBoundType FromValue(string value) => FromValueCore(value);
}
