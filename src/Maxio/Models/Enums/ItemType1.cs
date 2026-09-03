using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// Item type to add. Either Product or Component.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ItemType1>))]
public sealed record ItemType1 : StringEnum<ItemType1>
{
    private ItemType1(string value) : base(value)
    {
    }

    public static readonly ItemType1 Product = new("Product");

    public static ItemType1 FromValue(string value) => FromValueCore(value);
}
