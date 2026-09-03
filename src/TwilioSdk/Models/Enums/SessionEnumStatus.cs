using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Session. Can be: <c>open</c>, <c>in-progress</c>, <c>closed</c>, <c>failed</c>, or <c>unknown</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SessionEnumStatus>))]
public sealed record SessionEnumStatus : StringEnum<SessionEnumStatus>
{
    private SessionEnumStatus(string value) : base(value)
    {
    }

    public static readonly SessionEnumStatus Open = new("open");

    public static readonly SessionEnumStatus InProgress = new("in-progress");

    public static readonly SessionEnumStatus Closed = new("closed");

    public static readonly SessionEnumStatus Failed = new("failed");

    public static readonly SessionEnumStatus Unknown = new("unknown");

    public static SessionEnumStatus FromValue(string value) => FromValueCore(value);
}
