using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<FactorEnumTotpAlgorithms>))]
public sealed record FactorEnumTotpAlgorithms : StringEnum<FactorEnumTotpAlgorithms>
{
    private FactorEnumTotpAlgorithms(string value) : base(value)
    {
    }

    public static readonly FactorEnumTotpAlgorithms Sha1 = new("sha1");

    public static readonly FactorEnumTotpAlgorithms Sha256 = new("sha256");

    public static readonly FactorEnumTotpAlgorithms Sha512 = new("sha512");

    public static FactorEnumTotpAlgorithms FromValue(string value) => FromValueCore(value);
}
