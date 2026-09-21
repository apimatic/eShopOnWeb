using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of end user of the Bundle resource - can be <c>individual</c> or <c>business</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EndUserEnumType>))]
public sealed record EndUserEnumType : StringEnum<EndUserEnumType>
{
    private EndUserEnumType(string value) : base(value)
    {
    }

    public static readonly EndUserEnumType Individual = new("individual");

    public static readonly EndUserEnumType Business = new("business");

    public static EndUserEnumType FromValue(string value) => FromValueCore(value);
}
