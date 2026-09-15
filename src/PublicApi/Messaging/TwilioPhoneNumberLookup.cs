using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class TwilioPhoneNumberLookup : IPhoneNumberLookup
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);

            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: "line_type_intelligence,validation",
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

            var canonical = response.PhoneNumber;
            var hasValidationErrors = response.ValidationErrors is { Count: > 0 };
            if (response.Valid == false || hasValidationErrors || string.IsNullOrEmpty(canonical))
            {
                var reason = hasValidationErrors
                    ? "The provider does not consider this a usable destination (" +
                      string.Join(", ", response.ValidationErrors!.Select(e => e.Value)) + ")."
                    : "The provider does not consider this a usable destination.";
                return new PhoneNumberLookupResult(false, canonical, reason, false);
            }

            return new PhoneNumberLookupResult(true, canonical, null, false);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new PhoneNumberLookupResult(false, null, null, true);
            }

            if ((int)status is >= 400 and < 500)
            {
                return new PhoneNumberLookupResult(false, null, "The provider does not consider this a usable destination.", false);
            }

            return new PhoneNumberLookupResult(false, null, null, true);
        }
        catch (JsonException)
        {
            return new PhoneNumberLookupResult(false, null, null, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PhoneNumberLookupResult(false, null, null, true);
        }
    }
}
