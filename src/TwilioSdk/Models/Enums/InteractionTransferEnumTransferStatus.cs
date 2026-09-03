using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Transfer. Can be: <c>active</c>, <c>completed</c>, <c>failed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionTransferEnumTransferStatus>))]
public sealed record InteractionTransferEnumTransferStatus : StringEnum<InteractionTransferEnumTransferStatus>
{
    private InteractionTransferEnumTransferStatus(string value) : base(value)
    {
    }

    public static readonly InteractionTransferEnumTransferStatus Active = new("active");

    public static readonly InteractionTransferEnumTransferStatus Failed = new("failed");

    public static readonly InteractionTransferEnumTransferStatus Completed = new("completed");

    public static InteractionTransferEnumTransferStatus FromValue(string value) => FromValueCore(value);
}
