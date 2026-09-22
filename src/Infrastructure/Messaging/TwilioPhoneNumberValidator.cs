using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Validates a caller-supplied number via the provider's number-lookup API and returns its canonical
/// E.164 form. Lookup is served from a different host than the messaging API, so the messaging
/// <c>Twilio:BaseUrl</c> override does not affect it. The caller's number is never logged.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioPhoneNumberValidator> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.CallTimeoutSeconds));

        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null, ct: cts.Token);

            var usable = response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber);
            return new PhoneValidationResult(usable, usable ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            // A 404 from lookup means "not a valid number" — an outcome, not a fault.
            if ((int)status == 404)
                return new PhoneValidationResult(false, null);

            _logger.LogWarning("Number-lookup API error: HTTP {Status}.", (int)status);
            throw new ProviderGatewayException($"Number lookup returned HTTP {(int)status}.", ex, status);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Number-lookup API returned an unprocessable response: {Error}", ex.Message);
            throw new ProviderGatewayException("The number-lookup response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning("Number-lookup API was unreachable or timed out: {Error}", ex.Message);
            throw new ProviderGatewayException("Number lookup unreachable or timed out.", ex);
        }
    }
}
