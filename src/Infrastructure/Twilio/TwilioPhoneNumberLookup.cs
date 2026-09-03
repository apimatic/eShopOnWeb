using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly TwilioSdkClient _client;

    public TwilioPhoneNumberLookup(TwilioSdkClient client)
    {
        _client = client;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence,line_status",
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
                    ct: ct),
                cancellationToken);

            if (response.Valid != true || string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                var reason = "The provider does not consider this a usable destination.";
                if (response.ValidationErrors is { Count: > 0 })
                {
                    reason = "The provider does not consider this a usable destination.";
                }

                return new PhoneNumberLookupResult(false, null, reason);
            }

            return new PhoneNumberLookupResult(true, response.PhoneNumber, null);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500 and not 401 and not 403)
            {
                return new PhoneNumberLookupResult(false, null, "The provider does not consider this a usable destination.");
            }

            throw new SmsProviderException("The phone number lookup could not be completed.", ex.Error.StatusCode);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.");
        }
        catch (DuplicateProviderWriteException)
        {
            throw new SmsProviderException("The lookup may already have reached the provider; a duplicate attempt was blocked.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsProviderException("The phone number provider is unreachable.", inner: ex);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }
}
