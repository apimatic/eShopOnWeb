using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ReplaceItemsEnumEndUserType>))]
public sealed record ReplaceItemsEnumEndUserType : StringEnum<ReplaceItemsEnumEndUserType>
{
    private ReplaceItemsEnumEndUserType(string value) : base(value)
    {
    }

    public static readonly ReplaceItemsEnumEndUserType Individual = new("individual");

    public static readonly ReplaceItemsEnumEndUserType Business = new("business");

    public static ReplaceItemsEnumEndUserType FromValue(string value) => FromValueCore(value);
}
