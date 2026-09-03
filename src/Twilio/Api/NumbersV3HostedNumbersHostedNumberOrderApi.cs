using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class NumbersV3HostedNumbersHostedNumberOrderApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV3HostedNumbersHostedNumberOrderApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Host a phone number's capability on Twilio's platform.
    /// </summary>
    /// <param name="phoneNumber"></param>
    /// <param name="smsCapability"></param>
    /// <param name="accountSid"></param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="ccEmails"></param>
    /// <param name="smsUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="statusCallbackUrl"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="smsApplicationSid"></param>
    /// <param name="addressSid"></param>
    /// <param name="email"></param>
    /// <param name="verificationType"></param>
    /// <param name="verificationDocumentSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV3HostedNumbersHostedNumberOrder"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<NumbersV3HostedNumbersHostedNumberOrder> CreateHostedNumbersHostedNumberOrder(string phoneNumber,
        bool smsCapability,
        string? accountSid,
        string? friendlyName,
        string? uniqueName,
        IReadOnlyList<string>? ccEmails,
        string? smsUrl,
        AmdStatusCallbackMethod? smsMethod,
        string? smsFallbackUrl,
        AmdStatusCallbackMethod? smsFallbackMethod,
        string? statusCallbackUrl,
        AmdStatusCallbackMethod? statusCallbackMethod,
        string? smsApplicationSid,
        string? addressSid,
        string? email,
        DependentOrderEnumVerificationType? verificationType,
        string? verificationDocumentSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v3/HostedNumbers/HostedNumberOrders"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("phoneNumber", phoneNumber),
                    new Param("smsCapability", smsCapability),
                    new Param("accountSid", accountSid),
                    new Param("friendlyName", friendlyName),
                    new Param("uniqueName", uniqueName),
                    new Param("ccEmails", ccEmails),
                    new Param("smsUrl", smsUrl),
                    new Param("smsMethod", smsMethod),
                    new Param("smsFallbackUrl", smsFallbackUrl),
                    new Param("smsFallbackMethod", smsFallbackMethod),
                    new Param("statusCallbackUrl", statusCallbackUrl),
                    new Param("statusCallbackMethod", statusCallbackMethod),
                    new Param("smsApplicationSid", smsApplicationSid),
                    new Param("addressSid", addressSid),
                    new Param("email", email),
                    new Param("verificationType", verificationType),
                    new Param("verificationDocumentSid", verificationDocumentSid)]),
            JsonResponse.Create<NumbersV3HostedNumbersHostedNumberOrder>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
