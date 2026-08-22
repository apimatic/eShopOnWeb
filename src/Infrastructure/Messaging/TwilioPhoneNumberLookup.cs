using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(20);

    public TwilioPhoneNumberLookup(TwilioSdk.TwilioSdkClient client)
    {
        _client = client;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_callBudget);

            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: cts.Token);

            if (response.Valid != true || string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new PhoneNumberLookupResult(false, null, "The provider does not consider this a usable destination.");
            }

            return new PhoneNumberLookupResult(true, response.PhoneNumber, null);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500 && status is not 401 and not 403 and not 429)
            {
                return new PhoneNumberLookupResult(false, null, "The provider does not consider this a usable destination.");
            }

            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    private static MessagingProviderException Translate(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        return status switch
        {
            401 or 403 => new MessagingProviderException("The messaging provider is unavailable.", status, ex),
            429 => new MessagingProviderException("The messaging provider is temporarily unavailable.", status, ex),
            _ => new MessagingProviderException("The messaging provider rejected the lookup.", status, ex)
        };
    }
}
