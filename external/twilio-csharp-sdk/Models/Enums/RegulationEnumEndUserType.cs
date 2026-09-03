using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The type of End User the regulation requires - can be <c>individual</c> or <c>business</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RegulationEnumEndUserType>))]
public sealed record RegulationEnumEndUserType : StringEnum<RegulationEnumEndUserType>
{
    private RegulationEnumEndUserType(string value) : base(value)
    {
    }

    public static readonly RegulationEnumEndUserType Individual = new("individual");

    public static readonly RegulationEnumEndUserType Business = new("business");

    public static RegulationEnumEndUserType FromValue(string value) => FromValueCore(value);
}
