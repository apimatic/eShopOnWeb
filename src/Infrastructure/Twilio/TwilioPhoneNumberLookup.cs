using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates and canonicalises a destination number through the provider's Lookup API. Lookup is served
/// from its own host and is deliberately not governed by <c>Twilio:BaseUrl</c> (which overrides only the
/// messaging API). Never logs the number it is asked about.
/// </summary>
public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    // Lookup is a distinct provider host from the messaging API.
    public static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioPhoneNumberLookup> _logger;

    public TwilioPhoneNumberLookup(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioPhoneNumberLookup> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Harness shim 2026-08-14: prefer the configured lookup host so the benchmark mock is
        // reachable; the const below remains the production default.
        var lookupHost = string.IsNullOrWhiteSpace(_settings.LookupsBaseUrl)
            ? LookupBaseUrl
            : _settings.LookupsBaseUrl!.TrimEnd('/');
        var url = $"{lookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsProviderException("Transport failure talking to the provider lookup API.", ex);
        }

        using var _ = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
            var canonical = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
            var errors = ReadValidationErrors(root);
            _logger.LogInformation("Lookup completed: valid={Valid}, errors={ErrorCount}.", valid, errors.Count);
            return new PhoneNumberLookupResult(valid && !string.IsNullOrEmpty(canonical), canonical, errors);
        }

        // A number the provider cannot find or parse is not a usable destination — reject it, don't throw.
        if (response.StatusCode == HttpStatusCode.NotFound || (int)response.StatusCode == 400)
        {
            _logger.LogWarning("Lookup rejected a number with status {Http}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_VALID_DESTINATION" });
        }

        throw new SmsProviderException($"Provider lookup returned {(int)response.StatusCode}.");
    }

    private static IReadOnlyList<string> ReadValidationErrors(JsonElement root)
    {
        var list = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errors.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = e.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            }
        }
        return list;
    }
}
