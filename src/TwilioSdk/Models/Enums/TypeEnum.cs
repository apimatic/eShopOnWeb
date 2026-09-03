using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The valid credential types supported by the API.
/// The values of this enumeration are used for versioning the <c>AuthenticatorAssertion</c> and <c>AuthenticatorAttestation</c> structures according to the type of the authenticator.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TypeEnum>))]
public sealed record TypeEnum : StringEnum<TypeEnum>
{
    private TypeEnum(string value) : base(value)
    {
    }

    public static readonly TypeEnum PublicKey = new("public-key");

    public static TypeEnum FromValue(string value) => FromValueCore(value);
}
