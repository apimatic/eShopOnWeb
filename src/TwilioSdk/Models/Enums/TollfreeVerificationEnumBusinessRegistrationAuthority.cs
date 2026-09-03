using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The organizational authority for business registrations. Required for all business types except SOLE_PROPRIETOR.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumBusinessRegistrationAuthority>))]
public sealed record TollfreeVerificationEnumBusinessRegistrationAuthority : StringEnum<TollfreeVerificationEnumBusinessRegistrationAuthority>
{
    private TollfreeVerificationEnumBusinessRegistrationAuthority(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Ein = new("EIN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Cbn = new("CBN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Crn = new("CRN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority ProvincialNumber = new("PROVINCIAL_NUMBER");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Vat = new("VAT");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Acn = new("ACN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Abn = new("ABN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Brn = new("BRN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Siren = new("SIREN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Siret = new("SIRET");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Nzbn = new("NZBN");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority UStIdNr = new("USt-IdNr");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Cif = new("CIF");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Nif = new("NIF");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Cnpj = new("CNPJ");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Uid = new("UID");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Neq = new("NEQ");

    public static readonly TollfreeVerificationEnumBusinessRegistrationAuthority Other = new("OTHER");

    public static TollfreeVerificationEnumBusinessRegistrationAuthority FromValue(string value) =>
        FromValueCore(value);
}
