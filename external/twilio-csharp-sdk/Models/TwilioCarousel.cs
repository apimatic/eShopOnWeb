using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// twilio/carousel templates allow you to send a single text message accompanied by a set of up to 10 carousel cards in a horizontally scrollable view
/// </summary>
public record TwilioCarousel
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("cards")]
    public required IReadOnlyList<CarouselCard> Cards { get; init; }
}
