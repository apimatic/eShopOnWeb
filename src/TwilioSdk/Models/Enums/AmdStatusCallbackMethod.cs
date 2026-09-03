using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>amd_status_callback</c> URL. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>., The HTTP method we use to call <c>fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>., The HTTP method we should use to call <c>fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>., The HTTP method we use to call <c>inbound_request_url</c>. Can be <c>GET</c> or <c>POST</c>., The HTTP method we should use to call <c>inbound_request_url</c>. Can be <c>GET</c> or <c>POST</c> and the default is <c>POST</c>., The method to be used when calling the webhook's URL., The HTTP method that should be used to request the SmsFallbackUrl. Must be either <c>GET</c> or <c>POST</c>. This will be copied onto the IncomingPhoneNumber resource., The HTTP method that should be used to request the SmsUrl. Must be either <c>GET</c> or <c>POST</c>.  This will be copied onto the IncomingPhoneNumber resource., Optional. The Status Callback Method attached to the IncomingPhoneNumber resource., The HTTP method that should be used to request the SmsFallbackUrl. Must be either <c>GET</c> or <c>POST</c>. This will be copied onto the IncomingPhoneNumber resource., The HTTP method that should be used to request the SmsUrl. Must be either <c>GET</c> or <c>POST</c>.  This will be copied onto the IncomingPhoneNumber resource., Optional. The Status Callback Method attached to the IncomingPhoneNumber resource., The HTTP method used to call <c>status_callback</c>. Can be: <c>POST</c> or <c>GET</c>, defaults to <c>POST</c>., The HTTP method we should use to call <c>status_callback</c>. Can be <c>POST</c> or <c>GET</c> and defaults to <c>POST</c>., The HTTP method Twilio uses to call <c>status_callback</c>. Can be <c>POST</c> or <c>GET</c> and defaults to <c>POST</c>., The HTTP method we should use to call <c>status_callback</c>. Can be: <c>POST</c> or <c>GET</c> and the default is <c>POST</c>., The HTTP method Twilio should use to call <c>status_callback</c>. Can be <c>POST</c> or <c>GET</c>., The HTTP method to be used when sending a webhook request., The HTTP method to be used when sending a webhook request., The HTTP method to be used when sending a webhook request. One of <c>GET</c> or <c>POST</c>., HTTP method used to invoke the webhook URL., The HTTP method we should use to call <c>conference_recording_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>., The HTTP method we should use to call <c>conference_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>., The HTTP method we should use when we call <c>recording_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>., The HTTP method we should use to call <c>status_callback</c>. Can be: <c>POST</c> or <c>GET</c> and the default is <c>POST</c>., The HTTP method we should use to call <c>wait_url</c>. Can be <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. When using a static audio file, this should be <c>GET</c> so that we can cache the file., The Webhook Method of Global Webhook Configuration. One of <c>POST</c> or <c>GET</c>., HTTP method provided for status callback URL.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AmdStatusCallbackMethod>))]
public sealed record AmdStatusCallbackMethod : StringEnum<AmdStatusCallbackMethod>
{
    private AmdStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly AmdStatusCallbackMethod Get = new("GET");

    public static readonly AmdStatusCallbackMethod Post = new("POST");

    public static AmdStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
