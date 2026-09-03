using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the hosted number order. Can be: <c>twilio-processing</c>, <c>received</c>, <c>pending-verification</c>, <c>verified</c>, <c>pending-loa</c>, <c>carrier-processing</c>, <c>testing</c>, <c>completed</c>, <c>failed</c>, or <c>action-required</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<HostedNumberOrderEnumStatus>))]
public sealed record HostedNumberOrderEnumStatus : StringEnum<HostedNumberOrderEnumStatus>
{
    private HostedNumberOrderEnumStatus(string value) : base(value)
    {
    }

    public static readonly HostedNumberOrderEnumStatus TwilioProcessing = new("twilio-processing");

    public static readonly HostedNumberOrderEnumStatus Received = new("received");

    public static readonly HostedNumberOrderEnumStatus PendingVerification = new("pending-verification");

    public static readonly HostedNumberOrderEnumStatus Verified = new("verified");

    public static readonly HostedNumberOrderEnumStatus PendingLoa = new("pending-loa");

    public static readonly HostedNumberOrderEnumStatus CarrierProcessing = new("carrier-processing");

    public static readonly HostedNumberOrderEnumStatus Testing = new("testing");

    public static readonly HostedNumberOrderEnumStatus Completed = new("completed");

    public static readonly HostedNumberOrderEnumStatus Failed = new("failed");

    public static readonly HostedNumberOrderEnumStatus ActionRequired = new("action-required");

    public static HostedNumberOrderEnumStatus FromValue(string value) => FromValueCore(value);
}
