using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of address registration with emergency services. A registered emergency address will be used during handling of emergency calls from this number.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus>))]
public sealed record IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus : StringEnum<IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus>
{
    private IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus Registered = new("registered");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus Unregistered = new("unregistered");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus PendingRegistration = new("pending-registration");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus RegistrationFailure = new("registration-failure");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus PendingUnregistration = new("pending-unregistration");

    public static readonly IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus UnregistrationFailure = new("unregistration-failure");

    public static IncomingPhoneNumberTollFreeEnumEmergencyAddressStatus FromValue(string value) =>
        FromValueCore(value);
}
