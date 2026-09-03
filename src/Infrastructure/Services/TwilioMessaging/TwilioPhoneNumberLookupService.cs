using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Services.TwilioMessaging;

public class TwilioPhoneNumberLookupService : IPhoneNumberLookupService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);

    private readonly TwilioSdkClient _client;
    private readonly ILogger<TwilioPhoneNumberLookupService> _logger;

    public TwilioPhoneNumberLookupService(TwilioSdkClient client, ILogger<TwilioPhoneNumberLookupService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);

            var lookup = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: "line_type_intelligence",
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
                requestOptions: null,
                ct: cts.Token);

            var usable = lookup.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber);
            if (!usable)
            {
                _logger.LogInformation("Provider rejected a contact number as unusable.");
            }

            return new PhoneNumberLookupResult
            {
                IsUsable = usable,
                CanonicalNumber = lookup.PhoneNumber
            };
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Phone number lookup failed with provider status {StatusCode}.", status);
            if (status is 401 or 403 or 429 || status >= 500)
            {
                throw new TwilioProviderException("The messaging provider is unavailable.", MapBoundaryStatus(status), ex);
            }

            throw new ContactNumberRejectedException("The number is not a usable destination.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Phone number lookup returned a response that could not be processed.");
            throw new TwilioProviderException("The provider returned a response that could not be processed.", 502, ex);
        }
        catch (TwilioDuplicateWriteRefusedException)
        {
            throw new TwilioProviderException("The provider call ended with an unknown outcome.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning("Phone number lookup could not reach the provider.");
            throw new TwilioProviderException("The messaging provider is unavailable.", 502, ex);
        }
    }

    private static int MapBoundaryStatus(int providerStatus) =>
        providerStatus switch
        {
            401 or 403 => 502,
            429 => 503,
            _ => providerStatus >= 500 ? 502 : providerStatus
        };
}
