using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The parameter displays if emergency calling is enabled for this number. Active numbers may place emergency calls by dialing valid emergency numbers for the country.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberEnumEmergencyStatus>))]
public sealed record IncomingPhoneNumberEnumEmergencyStatus : StringEnum<IncomingPhoneNumberEnumEmergencyStatus>
{
    private IncomingPhoneNumberEnumEmergencyStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberEnumEmergencyStatus Active = new("Active");

    public static readonly IncomingPhoneNumberEnumEmergencyStatus Inactive = new("Inactive");

    public static IncomingPhoneNumberEnumEmergencyStatus FromValue(string value) => FromValueCore(value);
}
