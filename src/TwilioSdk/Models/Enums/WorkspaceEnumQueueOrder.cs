using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of TaskQueue to prioritize when Workers are receiving Tasks from both types of TaskQueues. Can be: <c>LIFO</c> or <c>FIFO</c> and the default is <c>FIFO</c>. For more information, see <see href="https://www.twilio.com/docs/taskrouter/queue-ordering-last-first-out-lifo">Queue Ordering</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WorkspaceEnumQueueOrder>))]
public sealed record WorkspaceEnumQueueOrder : StringEnum<WorkspaceEnumQueueOrder>
{
    private WorkspaceEnumQueueOrder(string value) : base(value)
    {
    }

    public static readonly WorkspaceEnumQueueOrder Fifo = new("FIFO");

    public static readonly WorkspaceEnumQueueOrder Lifo = new("LIFO");

    public static WorkspaceEnumQueueOrder FromValue(string value) => FromValueCore(value);
}
