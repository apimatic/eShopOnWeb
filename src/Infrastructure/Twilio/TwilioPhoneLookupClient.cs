using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's Lookups v2 API, built to the <c>twilio_lookups_v2</c>
/// OpenAPI contract. Served from Twilio's own lookups host (not the messaging base URL).
/// Performs no logging so the number being looked up never reaches a log sink.
/// </summary>
public class TwilioPhoneLookupClient : ITwilioPhoneLookupClient
{
    private readonly HttpClient _http;

    public TwilioPhoneLookupClient(HttpClient http, IOptions<TwilioOptions> options)
    {
        _http = http;
        _http.BaseAddress ??= new Uri(TwilioOptions.LookupsBaseUrl + "/");
        _http.DefaultRequestHeaders.Authorization = TwilioMessagingClient.BasicAuthHeader(options.Value);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var resource = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(resource, cancellationToken);

        // A number Twilio cannot parse at all comes back as 404 with code 20404 — treat that
        // as "not a usable destination" rather than a transport failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new PhoneLookupResult { Valid = false };

        await TwilioResponseReader.EnsureSuccessAsync(response, cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errs.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);
        }

        return new PhoneLookupResult { Valid = valid, PhoneNumber = canonical, ValidationErrors = errors };
    }
}
