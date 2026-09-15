using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly HttpClient _client;

    public TwilioPhoneNumberValidator(HttpClient client, IOptions<TwilioOptions> options)
    {
        _client = client;
        TwilioClientSupport.Configure(_client, TwilioOptions.LookupsBaseUrl, options.Value);
    }

    public async Task<PhoneNumberValidation> ValidateAsync(string number,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return new PhoneNumberValidation(false, null, new[] { "NOT_A_NUMBER" });
        }

        // FetchPhoneNumber from twilio_lookups_v2.yaml. No optional paid fields are requested.
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(number.Trim())}";
        using var response = await _client.GetAsync(path, cancellationToken);
        await TwilioClientSupport.EnsureSuccessAsync(response, "phone-number validation", cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<LookupResponse>(
            TwilioClientSupport.JsonOptions, cancellationToken)
            ?? throw new ProviderRequestException("phone-number validation");

        var valid = result.Valid && !string.IsNullOrWhiteSpace(result.PhoneNumber);
        return new PhoneNumberValidation(valid, valid ? result.PhoneNumber : null,
            result.ValidationErrors ?? new List<string>());
    }
}
