using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AllocationPreviewDirection>))]
public sealed record AllocationPreviewDirection : StringEnum<AllocationPreviewDirection>
{
    private AllocationPreviewDirection(string value) : base(value)
    {
    }

    public static readonly AllocationPreviewDirection Upgrade = new("upgrade");

    public static readonly AllocationPreviewDirection Downgrade = new("downgrade");

    public static AllocationPreviewDirection FromValue(string value) => FromValueCore(value);
}
