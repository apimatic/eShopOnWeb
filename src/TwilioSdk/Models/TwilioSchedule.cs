using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// twilio/schedule templates allow us to send a message with a schedule with different time slots
/// </summary>
public record TwilioSchedule
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("timeSlots")]
    public required string TimeSlots { get; init; }
}
