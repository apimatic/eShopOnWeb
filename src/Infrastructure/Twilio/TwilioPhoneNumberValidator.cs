using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates a caller-typed number against the provider's Lookup API (v2) and returns the
/// provider's canonical E.164 form. Runs on the Lookups host, which the messaging base-URL
/// override deliberately does not govern.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;

    public TwilioPhoneNumberValidator(TwilioSdkClient client)
    {
        _client = client;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            var lookup = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null,
                countryCode: null,
                firstName: null,
                lastName: null,
                addressLine1: null,
                addressLine2: null,
                city: null,
                state: null,
                postalCode: null,
                addressCountryCode: null,
                nationalId: null,
                dateOfBirth: null,
                lastVerifiedDate: null,
                verificationSid: null,
                partnerSubId: null,
                ct: cts.Token);

            return new PhoneNumberValidationResult
            {
                IsValid = lookup.Valid == true,
                CanonicalNumber = lookup.Valid == true ? lookup.PhoneNumber : null,
                ValidationErrors = lookup.ValidationErrors?.Select(e => e.Value).ToList()
                    ?? new System.Collections.Generic.List<string>()
            };
        }
        catch (SdkException<RawError> ex)
        {
            // A 4xx here (other than our own credential/quota faults) is the provider saying the
            // number is not a usable destination — an outcome, not an error.
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500 and not 401 and not 403 and not 429)
            {
                return new PhoneNumberValidationResult
                {
                    IsValid = false,
                    ValidationErrors = new System.Collections.Generic.List<string> { "not a usable destination" }
                };
            }

            throw new SmsProviderException(
                $"The messaging provider rejected the lookup request (HTTP {status}).", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // An unreadable body is NOT a "number is invalid" fact — never map a parse failure
            // onto a domain absence.
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider could not be reached.", null, ex);
        }
    }
}
