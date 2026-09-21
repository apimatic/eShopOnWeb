using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Defaults to checking
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BankAccountType>))]
public sealed record BankAccountType : StringEnum<BankAccountType>
{
    private BankAccountType(string value) : base(value)
    {
    }

    public static readonly BankAccountType Checking = new("checking");

    public static readonly BankAccountType Savings = new("savings");

    public static BankAccountType FromValue(string value) => FromValueCore(value);
}
