using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of this account. Usually <c>active</c>, but can be <c>suspended</c> or <c>closed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AccountEnumStatus>))]
public sealed record AccountEnumStatus : StringEnum<AccountEnumStatus>
{
    private AccountEnumStatus(string value) : base(value)
    {
    }

    public static readonly AccountEnumStatus Active = new("active");

    public static readonly AccountEnumStatus Suspended = new("suspended");

    public static readonly AccountEnumStatus Closed = new("closed");

    public static AccountEnumStatus FromValue(string value) => FromValueCore(value);
}
