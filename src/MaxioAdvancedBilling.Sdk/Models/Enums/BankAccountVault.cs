using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The vault that stores the payment profile with the provided vault_token. Use <c>bogus</c> for testing.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BankAccountVault>))]
public sealed record BankAccountVault : StringEnum<BankAccountVault>
{
    private BankAccountVault(string value) : base(value)
    {
    }

    public static readonly BankAccountVault Authorizenet = new("authorizenet");

    public static readonly BankAccountVault BlueSnap = new("blue_snap");

    public static readonly BankAccountVault Bogus = new("bogus");

    public static readonly BankAccountVault Forte = new("forte");

    public static readonly BankAccountVault Gocardless = new("gocardless");

    public static readonly BankAccountVault MaxioPayments = new("maxio_payments");

    public static readonly BankAccountVault Maxp = new("maxp");

    public static readonly BankAccountVault StripeConnect = new("stripe_connect");

    public static BankAccountVault FromValue(string value) => FromValueCore(value);
}
