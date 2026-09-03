using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RestrictionType>))]
public sealed record RestrictionType : StringEnum<RestrictionType>
{
    private RestrictionType(string value) : base(value)
    {
    }

    public static readonly RestrictionType Component = new("Component");

    public static readonly RestrictionType Product = new("Product");

    public static RestrictionType FromValue(string value) => FromValueCore(value);
}
