using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The account type for ISV Account Type Migration. Set to 'ISV' or 'ISVSubAccount' to configure, empty string to clear, or omit to preserve the existing value.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AccountType>))]
public sealed record AccountType : StringEnum<AccountType>
{
    private AccountType(string value) : base(value)
    {
    }

    public static readonly AccountType Isv = new("ISV");

    public static readonly AccountType IsvSubAccount = new("ISVSubAccount");

    public static AccountType FromValue(string value) => FromValueCore(value);
}
