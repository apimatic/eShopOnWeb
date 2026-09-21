using System;
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

namespace TwilioSdk.Api;

public sealed class ProxyV1PhoneNumber
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProxyV1PhoneNumber(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a Phone Number to a Service's Proxy Number Pool.
    /// </summary>
    /// <param name="serviceSid">The SID parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> resource of the new PhoneNumber resource.</param>
    /// <param name="sid"></param>
    /// <param name="phoneNumber"></param>
    /// <param name="isReserved"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServicePhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a Phone Number to a Service's Proxy Number Pool.
    /// </remarks>
    public Task<ProxyV1ServicePhoneNumber> CreatePhoneNumber2(string serviceSid,
        string? sid,
        string? phoneNumber,
        bool? isReserved,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/PhoneNumbers"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Sid", sid),
                    new Param("PhoneNumber", phoneNumber),
                    new Param("IsReserved", isReserved)]),
            JsonResponse.Create<ProxyV1ServicePhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Phone Number from a Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the PhoneNumber resource to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the PhoneNumber resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Phone Number from a Service.
    /// </remarks>
    public Task DeletePhoneNumber2(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/PhoneNumbers/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
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
    /// Fetch a specific Phone Number.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the PhoneNumber resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the PhoneNumber resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServicePhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Phone Number.
    /// </remarks>
    public Task<ProxyV1ServicePhoneNumber> FetchPhoneNumber4(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/PhoneNumbers/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProxyV1ServicePhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Phone Numbers in the Proxy Number Pool for a Service. A maximum of 100 records will be returned per page.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the PhoneNumber resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListPhoneNumberResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Phone Numbers in the Proxy Number Pool for a Service. A maximum of 100 records will be returned per page.
    /// </remarks>
    public Task<ListPhoneNumberResponse1> ListPhoneNumber2(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/PhoneNumbers"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPhoneNumberResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Proxy Number.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the PhoneNumber resource to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the PhoneNumber resource to update.</param>
    /// <param name="isReserved"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServicePhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Proxy Number.
    /// </remarks>
    public Task<ProxyV1ServicePhoneNumber> UpdatePhoneNumber(string serviceSid,
        string sid,
        bool? isReserved,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/PhoneNumbers/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("IsReserved", isReserved)]),
            JsonResponse.Create<ProxyV1ServicePhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
