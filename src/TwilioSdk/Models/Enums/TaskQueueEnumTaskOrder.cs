using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// How Tasks will be assigned to Workers. Set this parameter to <c>LIFO</c> to assign most recently created Task first or <c>FIFO</c> to assign the oldest Task. Default is FIFO. <see href="https://www.twilio.com/docs/taskrouter/queue-ordering-last-first-out-lifo">Click here</see> to learn more.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TaskQueueEnumTaskOrder>))]
public sealed record TaskQueueEnumTaskOrder : StringEnum<TaskQueueEnumTaskOrder>
{
    private TaskQueueEnumTaskOrder(string value) : base(value)
    {
    }

    public static readonly TaskQueueEnumTaskOrder Fifo = new("FIFO");

    public static readonly TaskQueueEnumTaskOrder Lifo = new("LIFO");

    public static TaskQueueEnumTaskOrder FromValue(string value) => FromValueCore(value);
}
