using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Defaults to personal
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BankAccountHolderType>))]
public sealed record BankAccountHolderType : StringEnum<BankAccountHolderType>
{
    private BankAccountHolderType(string value) : base(value)
    {
    }

    public static readonly BankAccountHolderType Personal = new("personal");

    public static readonly BankAccountHolderType Business = new("business");

    public static BankAccountHolderType FromValue(string value) => FromValueCore(value);
}
