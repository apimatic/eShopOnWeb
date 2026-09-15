using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private static readonly Uri LookupsBaseUri = new("https://lookups.twilio.com");
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string number,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new MessagingProviderException("Twilio account credentials are not configured.");
        }
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(number)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LookupsBaseUri, path));
        TwilioHttp.ApplyBasicAuthentication(request, _options);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        var valid = root.TryGetProperty("valid", out var validElement) && validElement.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var phoneElement) ? phoneElement.GetString() : null;
        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errorsElement.EnumerateArray())
            {
                if (error.GetString() is { } value)
                {
                    errors.Add(value);
                }
            }
        }

        return new PhoneNumberValidationResult(valid && !string.IsNullOrWhiteSpace(canonical), canonical, errors);
    }
}
