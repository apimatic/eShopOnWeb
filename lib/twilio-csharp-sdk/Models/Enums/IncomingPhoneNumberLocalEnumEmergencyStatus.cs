using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The parameter displays if emergency calling is enabled for this number. Active numbers may place emergency calls by dialing valid emergency numbers for the country.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberLocalEnumEmergencyStatus>))]
public sealed record IncomingPhoneNumberLocalEnumEmergencyStatus : StringEnum<IncomingPhoneNumberLocalEnumEmergencyStatus>
{
    private IncomingPhoneNumberLocalEnumEmergencyStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberLocalEnumEmergencyStatus Active = new("Active");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyStatus Inactive = new("Inactive");

    public static IncomingPhoneNumberLocalEnumEmergencyStatus FromValue(string value) => FromValueCore(value);
}
