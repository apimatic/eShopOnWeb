using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : IPhoneNumberLookupService
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;
        TwilioHttp.ConfigureBasicAuth(_http, _settings);
        _http.BaseAddress = new Uri("https://lookups.twilio.com/");
    }

    public async Task<LookupPhoneNumberResult> LookupAsync(string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber.Trim())}";
        using var response = await _http.GetAsync(path, cancellationToken);

        if ((int)response.StatusCode == 404)
        {
            return new LookupPhoneNumberResult(false, null, null, new[] { "NOT_A_NUMBER" });
        }

        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var body = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, TwilioHttp.JsonOptions, cancellationToken)
            ?? new LookupResponse();

        var errors = body.ValidationErrors ?? Array.Empty<string>();
        return new LookupPhoneNumberResult(body.Valid, body.PhoneNumber, body.NationalFormat, errors);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
