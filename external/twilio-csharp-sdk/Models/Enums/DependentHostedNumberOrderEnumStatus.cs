using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Status of an instance resource. It can hold one of the values: 1. opened 2. signing, 3. signed LOA, 4. canceled, 5. failed. See the section entitled <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource#status-values">Status Values</see> for more information on each of these statuses.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DependentHostedNumberOrderEnumStatus>))]
public sealed record DependentHostedNumberOrderEnumStatus : StringEnum<DependentHostedNumberOrderEnumStatus>
{
    private DependentHostedNumberOrderEnumStatus(string value) : base(value)
    {
    }

    public static readonly DependentHostedNumberOrderEnumStatus Received = new("received");

    public static readonly DependentHostedNumberOrderEnumStatus Verified = new("verified");

    public static readonly DependentHostedNumberOrderEnumStatus PendingLoa = new("pending-loa");

    public static readonly DependentHostedNumberOrderEnumStatus CarrierProcessing = new("carrier-processing");

    public static readonly DependentHostedNumberOrderEnumStatus Completed = new("completed");

    public static readonly DependentHostedNumberOrderEnumStatus Failed = new("failed");

    public static readonly DependentHostedNumberOrderEnumStatus ActionRequired = new("action-required");

    public static DependentHostedNumberOrderEnumStatus FromValue(string value) => FromValueCore(value);
}
