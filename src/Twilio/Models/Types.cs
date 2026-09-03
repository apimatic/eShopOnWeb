using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Content types
/// </summary>
public record Types
{
    /// <summary>
    /// Type containing only plain text-based content
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/text")]
    public TwilioText? TwilioText { get; init; }

    /// <summary>
    /// twilio/media is used to send file attachments, or to send long text via MMS in the US and Canada. As such, the twilio/media type must contain at least ONE of text or media content.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/media")]
    public TwilioMedia? TwilioMedia { get; init; }

    /// <summary>
    /// twilio/location type contains a location pin and an optional label, which can be used to enhance delivery notifications or connect recipients to physical experiences you offer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/location")]
    public TwilioLocation? TwilioLocation { get; init; }

    /// <summary>
    /// twilio/list-picker includes a menu of up to 10 options, which offers a simple way for users to make a selection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/list-picker")]
    public TwilioListPicker? TwilioListPicker { get; init; }

    /// <summary>
    /// twilio/call-to-action buttons let recipients tap to trigger actions such as launching a website or making a phone call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/call-to-action")]
    public TwilioCallToAction? TwilioCallToAction { get; init; }

    /// <summary>
    /// twilio/quick-reply templates let recipients tap, rather than type, to respond to the message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/quick-reply")]
    public TwilioQuickReply? TwilioQuickReply { get; init; }

    /// <summary>
    /// twilio/card is a structured template which can be used to send a series of related information. It must include a title and at least one additional field.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/card")]
    public TwilioCard? TwilioCard { get; init; }

    /// <summary>
    /// twilio/catalog type lets recipients view list of catalog products, ask questions about products, order products.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/catalog")]
    public TwilioCatalog? TwilioCatalog { get; init; }

    /// <summary>
    /// twilio/carousel templates allow you to send a single text message accompanied by a set of up to 10 carousel cards in a horizontally scrollable view
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/carousel")]
    public TwilioCarousel? TwilioCarousel { get; init; }

    /// <summary>
    /// twilio/flows templates allow you to send multiple messages in a set order with text or select options
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/flows")]
    public TwilioFlows? TwilioFlows { get; init; }

    /// <summary>
    /// twilio/schedule templates allow us to send a message with a schedule with different time slots
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio/schedule")]
    public TwilioSchedule? TwilioSchedule { get; init; }

    /// <summary>
    /// whatsapp/card is a structured template which can be used to send a series of related information. It must include a body and at least one additional field.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whatsapp/card")]
    public WhatsappCard? WhatsappCard { get; init; }

    /// <summary>
    /// whatsApp/authentication templates let companies deliver WA approved one-time-password button.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whatsapp/authentication")]
    public WhatsappAuthentication? WhatsappAuthentication { get; init; }

    /// <summary>
    /// whatsapp/flows templates allow you to send multiple messages in a set order with text or select options
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whatsapp/flows")]
    public WhatsappFlows? WhatsappFlows { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
