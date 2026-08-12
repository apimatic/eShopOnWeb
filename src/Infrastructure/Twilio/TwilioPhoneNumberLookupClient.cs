using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Phone-number validator hand-written against twilio_lookups_v2 (GET /v2/PhoneNumbers/{PhoneNumber}
/// on lookups.twilio.com). Returns the provider's verdict and canonical E.164 form. The Lookups host
/// is fixed and is deliberately not affected by the messaging <c>Twilio:BaseUrl</c> override.
/// </summary>
public class TwilioPhoneNumberLookupClient : IPhoneNumberValidator
{
    private readonly HttpClient _http;
    private readonly ILogger<TwilioPhoneNumberLookupClient> _logger;

    public TwilioPhoneNumberLookupClient(HttpClient http, ILogger<TwilioPhoneNumberLookupClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // The number is the path segment; escape it so arbitrary caller input is transmitted safely.
        var requestUri = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider could not resolve the number at all — treat as not a usable destination.
            _logger.LogInformation("Phone number lookup returned not-found; treating as invalid.");
            return new PhoneNumberLookupResult { Valid = false };
        }

        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            string message = $"Twilio lookup failed with HTTP {(int)response.StatusCode}.";
            try
            {
                var error = await response.Content.ReadFromJsonAsync<TwilioErrorResource>(cancellationToken: cancellationToken);
                if (error is not null)
                {
                    code = error.Code;
                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        message = error.Message!;
                    }
                }
            }
            catch (JsonException)
            {
                // keep generic message
            }
            throw new TwilioApiException(response.StatusCode, code, message);
        }

        var resource = await response.Content.ReadFromJsonAsync<TwilioLookupResource>(cancellationToken: cancellationToken)
                       ?? new TwilioLookupResource();

        return new PhoneNumberLookupResult
        {
            Valid = resource.Valid,
            PhoneNumber = resource.PhoneNumber,
            ValidationErrors = resource.ValidationErrors ?? new List<string>()
        };
    }
}
