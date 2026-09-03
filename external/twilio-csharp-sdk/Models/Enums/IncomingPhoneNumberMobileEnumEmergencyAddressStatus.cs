using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of address registration with emergency services. A registered emergency address will be used during handling of emergency calls from this number.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberMobileEnumEmergencyAddressStatus>))]
public sealed record IncomingPhoneNumberMobileEnumEmergencyAddressStatus : StringEnum<IncomingPhoneNumberMobileEnumEmergencyAddressStatus>
{
    private IncomingPhoneNumberMobileEnumEmergencyAddressStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus Registered = new("registered");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus Unregistered = new("unregistered");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus PendingRegistration = new("pending-registration");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus RegistrationFailure = new("registration-failure");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus PendingUnregistration = new("pending-unregistration");

    public static readonly IncomingPhoneNumberMobileEnumEmergencyAddressStatus UnregistrationFailure = new("unregistration-failure");

    public static IncomingPhoneNumberMobileEnumEmergencyAddressStatus FromValue(string value) =>
        FromValueCore(value);
}
