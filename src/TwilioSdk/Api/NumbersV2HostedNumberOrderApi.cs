using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class NumbersV2HostedNumberOrderApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2HostedNumberOrderApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Host a phone number's capability on Twilio's platform.
    /// </summary>
    /// <param name="phoneNumber"></param>
    /// <param name="contactPhoneNumber"></param>
    /// <param name="addressSid"></param>
    /// <param name="email"></param>
    /// <param name="accountSid"></param>
    /// <param name="friendlyName"></param>
    /// <param name="ccEmails"></param>
    /// <param name="smsUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsCapability"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="statusCallbackUrl"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="smsApplicationSid"></param>
    /// <param name="contactTitle"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2HostedNumberOrder"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Host a phone number's capability on Twilio's platform.
    /// </remarks>
    public Task<NumbersV2HostedNumberOrder> CreateHostedNumberOrder(string phoneNumber,
        string contactPhoneNumber,
        string addressSid,
        string email,
        string? accountSid,
        string? friendlyName,
        IReadOnlyList<string>? ccEmails,
        string? smsUrl,
        AmdStatusCallbackMethod? smsMethod,
        string? smsFallbackUrl,
        bool? smsCapability,
        AmdStatusCallbackMethod? smsFallbackMethod,
        string? statusCallbackUrl,
        AmdStatusCallbackMethod? statusCallbackMethod,
        string? smsApplicationSid,
        string? contactTitle,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/Orders"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PhoneNumber", phoneNumber),
                    new Param("ContactPhoneNumber", contactPhoneNumber),
                    new Param("AddressSid", addressSid),
                    new Param("Email", email),
                    new Param("AccountSid", accountSid),
                    new Param("FriendlyName", friendlyName),
                    new Param("CcEmails", ccEmails),
                    new Param("SmsUrl", smsUrl),
                    new Param("SmsMethod", smsMethod),
                    new Param("SmsFallbackUrl", smsFallbackUrl),
                    new Param("SmsCapability", smsCapability),
                    new Param("SmsFallbackMethod", smsFallbackMethod),
                    new Param("StatusCallbackUrl", statusCallbackUrl),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("SmsApplicationSid", smsApplicationSid),
                    new Param("ContactTitle", contactTitle)]),
            JsonResponse.Create<NumbersV2HostedNumberOrder>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Cancel the HostedNumberOrder (only available when the status is in <c>received</c>).
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this HostedNumberOrder.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancel the HostedNumberOrder (only available when the status is in <c>received</c>).
    /// </remarks>
    public Task DeleteHostedNumberOrder(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/Orders/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific HostedNumberOrder.
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this HostedNumberOrder.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2HostedNumberOrder"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific HostedNumberOrder.
    /// </remarks>
    public Task<NumbersV2HostedNumberOrder> FetchHostedNumberOrder(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/Orders/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV2HostedNumberOrder>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of HostedNumberOrders belonging to the account initiating the request.
    /// </summary>
    /// <param name="status">The Status of this HostedNumberOrder. One of <c>received</c>, <c>pending-verification</c>, <c>verified</c>, <c>pending-loa</c>, <c>carrier-processing</c>, <c>testing</c>, <c>completed</c>, <c>failed</c>, or <c>action-required</c>.</param>
    /// <param name="smsCapability">Whether the SMS capability will be hosted on our platform. Can be <c>true</c> of <c>false</c>.</param>
    /// <param name="phoneNumber">An E164 formatted phone number hosted by this HostedNumberOrder.</param>
    /// <param name="incomingPhoneNumberSid">A 34 character string that uniquely identifies the IncomingPhoneNumber resource created by this HostedNumberOrder.</param>
    /// <param name="friendlyName">A human readable description of this resource, up to 128 characters.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListHostedNumberOrderResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of HostedNumberOrders belonging to the account initiating the request.
    /// </remarks>
    public Task<ListHostedNumberOrderResponse> ListHostedNumberOrder(DependentOrderEnumStatus? status,
        bool? smsCapability,
        string? phoneNumber,
        string? incomingPhoneNumberSid,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/Orders"),
            [],
            [new Param("Status", status),
                new Param("SmsCapability", smsCapability),
                new Param("PhoneNumber", phoneNumber),
                new Param("IncomingPhoneNumberSid", incomingPhoneNumberSid),
                new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListHostedNumberOrderResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Updates a specific HostedNumberOrder.
    /// </summary>
    /// <param name="sid">The SID of the HostedNumberOrder resource to update.</param>
    /// <param name="status"></param>
    /// <param name="verificationCallDelay"></param>
    /// <param name="verificationCallExtension"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2HostedNumberOrder"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a specific HostedNumberOrder.
    /// </remarks>
    public Task<NumbersV2HostedNumberOrder> UpdateHostedNumberOrder(string sid,
        DependentOrderEnumStatus status,
        int? verificationCallDelay,
        string? verificationCallExtension,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/Orders/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status),
                    new Param("VerificationCallDelay", verificationCallDelay),
                    new Param("VerificationCallExtension", verificationCallExtension)]),
            JsonResponse.Create<NumbersV2HostedNumberOrder>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
