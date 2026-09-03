using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// Indicates the type of metafield. A text metafield allows any string value. Dropdown and radio metafields have a set of values that can be selected. Defaults to 'text'.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MetafieldInput>))]
public sealed record MetafieldInput : StringEnum<MetafieldInput>
{
    private MetafieldInput(string value) : base(value)
    {
    }

    public static readonly MetafieldInput BalanceTracker = new("balance_tracker");

    public static readonly MetafieldInput Text = new("text");

    public static readonly MetafieldInput Radio = new("radio");

    public static readonly MetafieldInput Dropdown = new("dropdown");

    public static MetafieldInput FromValue(string value) => FromValueCore(value);
}
