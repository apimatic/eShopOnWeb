using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type30>))]
public sealed record Type30 : StringEnum<Type30>
{
    private Type30(string value) : base(value)
    {
    }

    public static readonly Type30 Added = new("added");

    public static readonly Type30 Removed = new("removed");

    public static readonly Type30 Changed = new("changed");

    public static Type30 FromValue(string value) => FromValueCore(value);
}
