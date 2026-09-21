using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The verification method of the card.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultCardVerificationMethod>))]
public sealed record VaultCardVerificationMethod : StringEnum<VaultCardVerificationMethod>
{
    private VaultCardVerificationMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// When an authorization or transaction is attempted this option will return a contingency and HATEOAS link only when local regulations require strong customer authentication, (e.g. 3DS in countries and use cases where it is mandated). The API caller should redirect the payer to the link so that they can authenticate themselves. In all cases, when an authorization is requested the AVS/CVV results will be returned in the response.
    /// </summary>
    public static readonly VaultCardVerificationMethod ScaWhenRequired = new("SCA_WHEN_REQUIRED");

    /// <summary>
    /// Selecting this option will attempt to force a strong customer authentication for the authorization/transaction. In countries where SCA has been defined and implemented it will result in a contingency and HATEOAS link being returned.  The API caller should redirect the payer to that link so that they can authenticate themselves against their issuing bank or other entity. As noted, the HATEOAS link is only available in all regions where strong authentication is supported, (e.g. in European countries where 3DS is live). Merchants can use this setting as an additional layer of security if they choose to. In all cases, when an authorization is requested the AVS/CVV results will be returned in the response.
    /// </summary>
    public static readonly VaultCardVerificationMethod ScaAlways = new("SCA_ALWAYS");

    public static VaultCardVerificationMethod FromValue(string value) => FromValueCore(value);
}
