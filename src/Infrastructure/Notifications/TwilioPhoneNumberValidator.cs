using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Validates phone numbers with the Twilio Lookup v2 API and returns Twilio's canonical
/// (E.164) form of the number. The Lookup API is served from lookups.twilio.com and is
/// not governed by the Twilio:BaseUrl messaging override.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(HttpClient httpClient, ILogger<TwilioPhoneNumberValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return PhoneNumberValidationResult.Invalid("A phone number is required.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phone number validation request failed.");
            return PhoneNumberValidationResult.Invalid("The phone number could not be validated at this time.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return PhoneNumberValidationResult.Invalid("The phone number is not a usable destination.");
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number validation returned status {StatusCode}.", (int)response.StatusCode);
            return PhoneNumberValidationResult.Invalid("The phone number could not be validated at this time.");
        }

        var lookup = await response.Content.ReadFromJsonAsync<LookupResponse>(JsonOptions, cancellationToken);
        if (lookup?.Valid == true && !string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            return PhoneNumberValidationResult.Valid(lookup.PhoneNumber);
        }

        var reasons = lookup?.ValidationErrors is { Length: > 0 } errors
            ? string.Join(", ", errors)
            : "not a usable destination";
        return PhoneNumberValidationResult.Invalid($"The phone number is invalid: {reasons}.");
    }

    private class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
