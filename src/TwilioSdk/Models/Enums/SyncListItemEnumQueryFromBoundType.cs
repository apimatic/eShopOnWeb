using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SyncListItemEnumQueryFromBoundType>))]
public sealed record SyncListItemEnumQueryFromBoundType : StringEnum<SyncListItemEnumQueryFromBoundType>
{
    private SyncListItemEnumQueryFromBoundType(string value) : base(value)
    {
    }

    public static readonly SyncListItemEnumQueryFromBoundType Inclusive = new("inclusive");

    public static readonly SyncListItemEnumQueryFromBoundType Exclusive = new("exclusive");

    public static SyncListItemEnumQueryFromBoundType FromValue(string value) => FromValueCore(value);
}
