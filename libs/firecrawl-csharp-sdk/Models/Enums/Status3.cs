using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status3>))]
public sealed record Status3 : StringEnum<Status3>
{
    private Status3(string value) : base(value)
    {
    }

    public static readonly Status3 Same = new("same");

    public static readonly Status3 New = new("new");

    public static readonly Status3 Changed = new("changed");

    public static readonly Status3 Removed = new("removed");

    public static readonly Status3 Error = new("error");

    public static Status3 FromValue(string value) => FromValueCore(value);
}
