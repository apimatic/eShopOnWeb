using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// The profile information for the sender.
/// </summary>
public record MessagingV2ChannelsSenderProfile
{
    /// <summary>
    /// The name of the sender. Required for WhatsApp senders and must follow <see href="https://www.facebook.com/business/help/757569725593362">Meta's display name guidelines</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The profile about text for the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("about")]
    public string? About { get; init; }

    /// <summary>
    /// The address of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// The description of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The logo URL of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    /// <summary>
    /// The banner URL of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; init; }

    /// <summary>
    /// The privacy URL of the sender. Must be a publicly accessible HTTP or HTTPS URI associated with the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("privacy_url")]
    public string? PrivacyUrl { get; init; }

    /// <summary>
    /// The terms of service URL of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("terms_of_service_url")]
    public string? TermsOfServiceUrl { get; init; }

    /// <summary>
    /// The color theme of the sender. Must be in hex format and have at least a 4:5:1 contrast ratio against white.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accent_color")]
    public string? AccentColor { get; init; }

    /// <summary>
    /// The messaging use case type for the RCS sender. Allowed values are <c>PROMOTIONAL</c>, <c>TRANSACTIONAL</c>, <c>OTP</c>, <c>MULTI_USE</c>. Defaults to <c>MULTI_USE</c> if not provided. Cannot be modified after launch.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_case")]
    public UseCase? UseCase { get; init; }

    /// <summary>
    /// The vertical of the sender. Allowed values are:
    /// - <c>Alcohol</c>
    /// - <c>Automotive</c>
    /// - <c>Beauty, Spa and Salon</c>
    /// - <c>Clothing and Apparel</c>
    /// - <c>Education</c>
    /// - <c>Entertainment</c>
    /// - <c>Event Planning and Service</c>
    /// - <c>Finance and Banking</c>
    /// - <c>Food and Grocery</c>
    /// - <c>Hotel and Lodging</c>
    /// - <c>Matrimony Service</c>
    /// - <c>Medical and Health</c>
    /// - <c>Non-profit</c>
    /// - <c>Online Gambling</c>
    /// - <c>OTC Drugs</c>
    /// - <c>Other</c>
    /// - <c>Physical Gambling</c>
    /// - <c>Professional Services</c>
    /// - <c>Public Service</c>
    /// - <c>Restaurant</c>
    /// - <c>Shopping and Retail</c>
    /// - <c>Travel and Transportation</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vertical")]
    public string? Vertical { get; init; }

    /// <summary>
    /// The websites of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("websites")]
    public object? Websites { get; init; }

    /// <summary>
    /// The emails of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emails")]
    public object? Emails { get; init; }

    /// <summary>
    /// The phone numbers of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_numbers")]
    public object? PhoneNumbers { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
