using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The vault that stores the payment profile with the provided vault_token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ApplePayVault>))]
public sealed record ApplePayVault : StringEnum<ApplePayVault>
{
    private ApplePayVault(string value) : base(value)
    {
    }

    public static readonly ApplePayVault BraintreeBlue = new("braintree_blue");

    public static ApplePayVault FromValue(string value) => FromValueCore(value);
}
