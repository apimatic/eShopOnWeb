using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// User Action on action to be performed after a successful payer approval.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultUserAction>))]
public sealed record VaultUserAction : StringEnum<VaultUserAction>
{
    private VaultUserAction(string value) : base(value)
    {
    }

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Setup Now button appears. Use this option when no additional inputs are needed from merchant site and to create the billing agreement immediately when the customer clicks Setup Now.
    /// </summary>
    public static readonly VaultUserAction SetupNow = new("SETUP_NOW");

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Continue button appears. Use this option when you want to redirect the customer from the completed payment page to the merchant site for additional inputs without immediately creating the billing agreement.
    /// </summary>
    public static readonly VaultUserAction Continue = new("CONTINUE");

    public static VaultUserAction FromValue(string value) => FromValueCore(value);
}
