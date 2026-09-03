using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Where a proxy number must be located relative to the participant identifier. Can be: <c>country</c>, <c>area-code</c>, or <c>extended-area-code</c>. The default value is <c>country</c> and more specific areas than <c>country</c> are only available in North America.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceEnumGeoMatchLevel>))]
public sealed record ServiceEnumGeoMatchLevel : StringEnum<ServiceEnumGeoMatchLevel>
{
    private ServiceEnumGeoMatchLevel(string value) : base(value)
    {
    }

    public static readonly ServiceEnumGeoMatchLevel AreaCode = new("area-code");

    public static readonly ServiceEnumGeoMatchLevel Overlay = new("overlay");

    public static readonly ServiceEnumGeoMatchLevel Radius = new("radius");

    public static readonly ServiceEnumGeoMatchLevel Country = new("country");

    public static ServiceEnumGeoMatchLevel FromValue(string value) => FromValueCore(value);
}
