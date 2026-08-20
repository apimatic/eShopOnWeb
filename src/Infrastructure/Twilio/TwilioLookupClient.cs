using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookup";
    private const string LookupHost = "https://lookups.twilio.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        ConfigureClient(_httpClient, _settings);
    }

    internal static void ConfigureClient(HttpClient httpClient, TwilioSettings settings)
    {
        httpClient.BaseAddress = new Uri(LookupHost);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyBasicAuth(httpClient, settings);
    }

    internal static void ApplyBasicAuth(HttpClient httpClient, TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            return;
        }

        var raw = Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public async Task<LookupNumberResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await TwilioHttpRetry.SendAsync(
            () => _httpClient.GetAsync(path, cancellationToken),
            allowRetryOnSuccessPath: true,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioHttpRetry.ToApiException(response, payload);
        }

        var parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions)
                     ?? new TwilioLookupResponse();

        return new LookupNumberResult(
            parsed.Valid,
            parsed.PhoneNumber,
            parsed.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio lookup is not configured.");
        }
    }
}
