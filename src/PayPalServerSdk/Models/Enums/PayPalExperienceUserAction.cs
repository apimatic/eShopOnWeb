using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Configures a Continue or Pay Now checkout flow.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalExperienceUserAction>))]
public sealed record PayPalExperienceUserAction : StringEnum<PayPalExperienceUserAction>
{
    private PayPalExperienceUserAction(string value) : base(value)
    {
    }

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Continue button appears. Use this option when the final amount is not known when the checkout flow is initiated and you want to redirect the customer to the merchant page without processing the payment.
    /// </summary>
    public static readonly PayPalExperienceUserAction Continue = new("CONTINUE");

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Pay Now button appears. Use this option when the final amount is known when the checkout is initiated and you want to process the payment immediately when the customer clicks Pay Now.
    /// </summary>
    public static readonly PayPalExperienceUserAction PayNow = new("PAY_NOW");

    public static PayPalExperienceUserAction FromValue(string value) => FromValueCore(value);
}
