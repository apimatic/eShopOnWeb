using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The valid credential types supported by the API. The values of this enumeration are used for versioning the <c>AuthenticatorAssertion</c> and <c>AuthenticatorAttestation</c> structures according to the type of the authenticator.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type1>))]
public sealed record Type1 : StringEnum<Type1>
{
    private Type1(string value) : base(value)
    {
    }

    public static readonly Type1 PublicKey = new("public-key");

    public static Type1 FromValue(string value) => FromValueCore(value);
}
