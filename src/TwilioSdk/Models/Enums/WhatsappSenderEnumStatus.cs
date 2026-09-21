using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the sender. Configuring: We are in the process of registering the sender. If your sender stays in this state for a long period of time it is possible that there is an issue with parameters you provided; PendingVerification: We have successfully registered the sender with WhatsApp and you should receive a code from their services; Configured: The sender has been successfully verified with WhatsApp and is all set to start sending messages; ConfigurationError - If configuration fails due to below possibilities: parameters provided were incorrect, Twilio account suspended or deleted, whatsapp api failed, Twilio internal error. VerificationError - If verification api fails, please check error_message for more details
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WhatsappSenderEnumStatus>))]
public sealed record WhatsappSenderEnumStatus : StringEnum<WhatsappSenderEnumStatus>
{
    private WhatsappSenderEnumStatus(string value) : base(value)
    {
    }

    public static readonly WhatsappSenderEnumStatus Configuring = new("Configuring");

    public static readonly WhatsappSenderEnumStatus PendingVerification = new("PendingVerification");

    public static readonly WhatsappSenderEnumStatus Configured = new("Configured");

    public static readonly WhatsappSenderEnumStatus ConfigurationError = new("ConfigurationError");

    public static readonly WhatsappSenderEnumStatus VerificationError = new("VerificationError");

    public static WhatsappSenderEnumStatus FromValue(string value) => FromValueCore(value);
}
