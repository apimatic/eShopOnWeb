using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The messaging use case type for the RCS sender. Allowed values are <c>PROMOTIONAL</c>, <c>TRANSACTIONAL</c>, <c>OTP</c>, <c>MULTI_USE</c>. Defaults to <c>MULTI_USE</c> if not provided. Cannot be modified after launch.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UseCase>))]
public sealed record UseCase : StringEnum<UseCase>
{
    private UseCase(string value) : base(value)
    {
    }

    public static readonly UseCase Promotional = new("PROMOTIONAL");

    public static readonly UseCase Transactional = new("TRANSACTIONAL");

    public static readonly UseCase Otp = new("OTP");

    public static readonly UseCase MultiUse = new("MULTI_USE");

    public static UseCase FromValue(string value) => FromValueCore(value);
}
