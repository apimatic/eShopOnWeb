using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of this call. Can be: <c>queued</c>, <c>ringing</c>, <c>in-progress</c>, <c>canceled</c>, <c>completed</c>, <c>failed</c>, <c>busy</c> or <c>no-answer</c>. See <see href="https://www.twilio.com/docs/voice/api/call-resource#call-status-values">Call Status Values</see> below for more information.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallEnumStatus>))]
public sealed record CallEnumStatus : StringEnum<CallEnumStatus>
{
    private CallEnumStatus(string value) : base(value)
    {
    }

    public static readonly CallEnumStatus Queued = new("queued");

    public static readonly CallEnumStatus Ringing = new("ringing");

    public static readonly CallEnumStatus InProgress = new("in-progress");

    public static readonly CallEnumStatus Completed = new("completed");

    public static readonly CallEnumStatus Busy = new("busy");

    public static readonly CallEnumStatus Failed = new("failed");

    public static readonly CallEnumStatus NoAnswer = new("no-answer");

    public static readonly CallEnumStatus Canceled = new("canceled");

    public static CallEnumStatus FromValue(string value) => FromValueCore(value);
}
