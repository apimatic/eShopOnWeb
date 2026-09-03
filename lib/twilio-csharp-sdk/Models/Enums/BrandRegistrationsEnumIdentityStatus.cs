using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// When a brand is registered, TCR will attempt to verify the identity of the brand based on the supplied information.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BrandRegistrationsEnumIdentityStatus>))]
public sealed record BrandRegistrationsEnumIdentityStatus : StringEnum<BrandRegistrationsEnumIdentityStatus>
{
    private BrandRegistrationsEnumIdentityStatus(string value) : base(value)
    {
    }

    public static readonly BrandRegistrationsEnumIdentityStatus SelfDeclared = new("SELF_DECLARED");

    public static readonly BrandRegistrationsEnumIdentityStatus Unverified = new("UNVERIFIED");

    public static readonly BrandRegistrationsEnumIdentityStatus Verified = new("VERIFIED");

    public static readonly BrandRegistrationsEnumIdentityStatus VettedVerified = new("VETTED_VERIFIED");

    public static BrandRegistrationsEnumIdentityStatus FromValue(string value) => FromValueCore(value);
}
