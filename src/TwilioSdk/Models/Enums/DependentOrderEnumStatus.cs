using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the hosted number order. Can be: <c>twilio-processing</c>, <c>received</c>, <c>pending-verification</c>, <c>verified</c>, <c>pending-loa</c>, <c>carrier-processing</c>, <c>testing</c>, <c>completed</c>, <c>failed</c>, or <c>action-required</c>., Status of this resource. It can hold one of the values: 1. Twilio Processing 2. Received, 3. Pending LOA, 4. Carrier Processing, 5. Completed, 6. Action Required, 7. Failed. See the <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/hosted-number-order-resource#status-values">HostedNumberOrders Status Values</see> section for more information on each of these statuses.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DependentOrderEnumStatus>))]
public sealed record DependentOrderEnumStatus : StringEnum<DependentOrderEnumStatus>
{
    private DependentOrderEnumStatus(string value) : base(value)
    {
    }

    public static readonly DependentOrderEnumStatus TwilioProcessing = new("twilio-processing");

    public static readonly DependentOrderEnumStatus Received = new("received");

    public static readonly DependentOrderEnumStatus PendingVerification = new("pending-verification");

    public static readonly DependentOrderEnumStatus Verified = new("verified");

    public static readonly DependentOrderEnumStatus PendingLoa = new("pending-loa");

    public static readonly DependentOrderEnumStatus CarrierProcessing = new("carrier-processing");

    public static readonly DependentOrderEnumStatus Testing = new("testing");

    public static readonly DependentOrderEnumStatus Completed = new("completed");

    public static readonly DependentOrderEnumStatus Failed = new("failed");

    public static readonly DependentOrderEnumStatus ActionRequired = new("action-required");

    public static DependentOrderEnumStatus FromValue(string value) => FromValueCore(value);
}
