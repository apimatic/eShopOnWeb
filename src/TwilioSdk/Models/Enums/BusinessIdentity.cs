using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Business customer type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BusinessIdentity>))]
public sealed record BusinessIdentity : StringEnum<BusinessIdentity>
{
    private BusinessIdentity(string value) : base(value)
    {
    }

    public static readonly BusinessIdentity Direct = new("DIRECT");

    public static readonly BusinessIdentity Isv = new("ISV");

    public static BusinessIdentity FromValue(string value) => FromValueCore(value);
}
