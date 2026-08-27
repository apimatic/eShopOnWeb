using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal sealed class TwilioMessageDto
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }
}

internal sealed class TwilioMessageListDto
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupDto
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

internal static class TwilioMessageMapper
{
    public static Microsoft.eShopWeb.ApplicationCore.Interfaces.ProviderMessage ToProviderMessage(TwilioMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Sid) || string.IsNullOrWhiteSpace(dto.Status))
        {
            throw new TwilioApiException(200, errorCode: null);
        }

        return new Microsoft.eShopWeb.ApplicationCore.Interfaces.ProviderMessage
        {
            Sid = dto.Sid,
            Status = dto.Status,
            ErrorCode = dto.ErrorCode,
            Body = dto.Body,
            Direction = dto.Direction,
            DateSent = ParseRfc2822(dto.DateSent),
            DateCreated = ParseRfc2822(dto.DateCreated)
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
