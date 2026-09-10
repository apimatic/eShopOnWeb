using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<PrepaymentMethod>))]
public sealed record PrepaymentMethod : StringEnum<PrepaymentMethod>
{
    private PrepaymentMethod(string value) : base(value)
    {
    }

    public static readonly PrepaymentMethod Check = new("check");

    public static readonly PrepaymentMethod Cash = new("cash");

    public static readonly PrepaymentMethod MoneyOrder = new("money_order");

    public static readonly PrepaymentMethod Ach = new("ach");

    public static readonly PrepaymentMethod PaypalAccount = new("paypal_account");

    public static readonly PrepaymentMethod CreditCard = new("credit_card");

    public static readonly PrepaymentMethod Other = new("other");

    public static PrepaymentMethod FromValue(string value) => FromValueCore(value);
}
