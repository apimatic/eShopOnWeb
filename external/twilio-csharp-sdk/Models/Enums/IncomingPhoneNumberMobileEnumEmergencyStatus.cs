using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The parameter displays if emergency calling is enabled for this number. Active numbers may place emergency calls by dialing valid emergency numbers for the country.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberMobileEnumEmergencyStatus>))]
public sealed record IncomingPhoneNumberMobileEnumEmergencyStatus : StringEnum<IncomingPhoneNumberMobileEnumEmergencyStatus>
{
    private IncomingPhoneNumberMobileEnumEmergencyStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberMobileEnumEmergencyStatus Active = new("Active");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyStatus Inactive = new("Inactive");

    public static IncomingPhoneNumberMobileEnumEmergencyStatus FromValue(string value) => FromValueCore(value);
}
