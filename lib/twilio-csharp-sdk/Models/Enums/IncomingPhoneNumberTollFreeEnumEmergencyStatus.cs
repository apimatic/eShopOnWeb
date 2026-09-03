using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The parameter displays if emergency calling is enabled for this number. Active numbers may place emergency calls by dialing valid emergency numbers for the country.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberTollFreeEnumEmergencyStatus>))]
public sealed record IncomingPhoneNumberTollFreeEnumEmergencyStatus : StringEnum<IncomingPhoneNumberTollFreeEnumEmergencyStatus>
{
    private IncomingPhoneNumberTollFreeEnumEmergencyStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyStatus Active = new("Active");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyStatus Inactive = new("Inactive");

    public static IncomingPhoneNumberTollFreeEnumEmergencyStatus FromValue(string value) =>
        FromValueCore(value);
}
