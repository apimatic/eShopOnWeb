using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private static readonly HashSet<string> RejectedLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        LineType.Landline.Value,
        LineType.Pager.Value,
        LineType.Voicemail.Value,
        LineType.Uan.Value
    };

    private readonly TwilioSdkClient _client;
    private readonly TimeSpan _callBudget;

    public TwilioPhoneNumberLookup(TwilioSdkClient client)
    {
        _client = client;
        _callBudget = TimeSpan.FromSeconds(20);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        LookupResponse response;
        try
        {
            response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: $"{Field.LineTypeIntelligence.Value},{Field.LineStatus.Value}",
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
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is >= 400 and < 500 and not 401 and not 403)
        {
            return new PhoneLookupResult
            {
                IsUsable = false,
                RejectionReason = "The provider does not consider this number a usable destination."
            };
        }
        catch (SdkException<RawError> ex)
        {
            throw MapProviderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SmsGatewayException("The provider returned a response that could not be processed.", statusCode: null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", statusCode: null, ex);
        }

        if (response.Valid == false)
        {
            return new PhoneLookupResult
            {
                IsUsable = false,
                RejectionReason = "The provider does not consider this number a usable destination."
            };
        }

        if (response.ValidationErrors is { Count: > 0 })
        {
            return new PhoneLookupResult
            {
                IsUsable = false,
                RejectionReason = "The provider does not consider this number a usable destination."
            };
        }

        var lineType = response.LineTypeIntelligence?.Type;
        if (!string.IsNullOrWhiteSpace(lineType) && RejectedLineTypes.Contains(lineType))
        {
            return new PhoneLookupResult
            {
                IsUsable = false,
                RejectionReason = "The provider does not consider this number a usable SMS destination."
            };
        }

        if (string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            throw new SmsGatewayException("The provider returned a response that could not be processed.");
        }

        return new PhoneLookupResult
        {
            IsUsable = true,
            CanonicalNumber = response.PhoneNumber
        };
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);
        return await call(cts.Token);
    }

    private static SmsGatewayException MapProviderError(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        if (status is 401 or 403)
        {
            return new SmsGatewayException("The messaging provider is unavailable.", status);
        }

        if (status == 429)
        {
            return new SmsGatewayException("The messaging provider is temporarily unavailable.", status);
        }

        if (status >= 400 && status < 500)
        {
            return new SmsGatewayException("The provider rejected the request.", status);
        }

        return new SmsGatewayException("The messaging provider is unavailable.", status);
    }
}
