using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of this account. Either <c>Trial</c> or <c>Full</c> if it's been upgraded
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AccountEnumType>))]
public sealed record AccountEnumType : StringEnum<AccountEnumType>
{
    private AccountEnumType(string value) : base(value)
    {
    }

    public static readonly AccountEnumType Trial = new("Trial");

    public static readonly AccountEnumType Full = new("Full");

    public static AccountEnumType FromValue(string value) => FromValueCore(value);
}
