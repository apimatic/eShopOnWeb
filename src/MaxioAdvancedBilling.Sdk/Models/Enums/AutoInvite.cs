using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(IntEnumConverter<AutoInvite>))]
public sealed record AutoInvite : IntEnum<AutoInvite>
{
    private AutoInvite(int value) : base(value)
    {
    }

    /// <summary>
    /// Do not send the invitation email.
    /// </summary>
    public static readonly AutoInvite Value0 = new(0);

    /// <summary>
    /// Automatically send the invitation email.
    /// </summary>
    public static readonly AutoInvite Value1 = new(1);

    public static AutoInvite FromValue(int value) => FromValueCore(value);
}
