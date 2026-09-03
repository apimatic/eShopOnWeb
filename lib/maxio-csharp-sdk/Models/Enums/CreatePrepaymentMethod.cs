using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// When the <c>method</c> specified is <c>"credit_card_on_file"</c>, the prepayment amount will be collected using the default credit card payment profile and applied to the prepayment account balance. This is especially useful for manual replenishment of prepaid subscriptions.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CreatePrepaymentMethod>))]
public sealed record CreatePrepaymentMethod : StringEnum<CreatePrepaymentMethod>
{
    private CreatePrepaymentMethod(string value) : base(value)
    {
    }

    public static readonly CreatePrepaymentMethod Check = new("check");

    public static readonly CreatePrepaymentMethod Cash = new("cash");

    public static readonly CreatePrepaymentMethod MoneyOrder = new("money_order");

    public static readonly CreatePrepaymentMethod Ach = new("ach");

    public static readonly CreatePrepaymentMethod PaypalAccount = new("paypal_account");

    public static readonly CreatePrepaymentMethod CreditCard = new("credit_card");

    public static readonly CreatePrepaymentMethod CreditCardOnFile = new("credit_card_on_file");

    public static readonly CreatePrepaymentMethod Other = new("other");

    public static CreatePrepaymentMethod FromValue(string value) => FromValueCore(value);
}
