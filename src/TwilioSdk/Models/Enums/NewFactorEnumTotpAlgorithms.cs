using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<NewFactorEnumTotpAlgorithms>))]
public sealed record NewFactorEnumTotpAlgorithms : StringEnum<NewFactorEnumTotpAlgorithms>
{
    private NewFactorEnumTotpAlgorithms(string value) : base(value)
    {
    }

    public static readonly NewFactorEnumTotpAlgorithms Sha1 = new("sha1");

    public static readonly NewFactorEnumTotpAlgorithms Sha256 = new("sha256");

    public static readonly NewFactorEnumTotpAlgorithms Sha512 = new("sha512");

    public static NewFactorEnumTotpAlgorithms FromValue(string value) => FromValueCore(value);
}
