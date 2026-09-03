using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// Item type to add. Either Product or Component.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ItemType>))]
public sealed record ItemType : StringEnum<ItemType>
{
    private ItemType(string value) : base(value)
    {
    }

    public static readonly ItemType Component = new("Component");

    public static ItemType FromValue(string value) => FromValueCore(value);
}
