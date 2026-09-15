using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient http, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;
        _http.BaseAddress ??= new Uri("https://lookups.twilio.com");
        _http.DefaultRequestHeaders.Authorization = TwilioHttp.BasicAuth(_settings);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        var url = $"/v2/PhoneNumbers/{encoded}?Fields={Uri.EscapeDataString("line_type_intelligence")}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"&CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await TwilioRequestSender.SendAsync(
            _http,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            retryServerErrors: true,
            _logger,
            cancellationToken);

        await TwilioRequestSender.EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<TwilioLookupDto>(payload, TwilioRequestSender.JsonOptions)
                  ?? new TwilioLookupDto();

        return new PhoneLookupResult(
            dto.Valid,
            dto.PhoneNumber,
            dto.NationalFormat,
            dto.LineTypeIntelligence?.ErrorCode is null ? dto.LineTypeIntelligence?.Type : null,
            dto.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>(),
            dto.LineTypeIntelligence?.ErrorCode);
    }
}
