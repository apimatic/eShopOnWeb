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

public sealed class MessagingV1PhoneNumber
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1PhoneNumber(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// A Messaging Service resource to add, fetch or remove phone numbers from a Messaging Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to create the resource under.</param>
    /// <param name="phoneNumberSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServicePhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServicePhoneNumber> CreatePhoneNumber(string serviceSid,
        string phoneNumberSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{ServiceSid}/PhoneNumbers"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PhoneNumberSid", phoneNumberSid)]),
            JsonResponse.Create<MessagingV1ServicePhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to add, fetch or remove phone numbers from a Messaging Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to delete the resource from.</param>
    /// <param name="sid">The SID of the PhoneNumber resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeletePhoneNumber(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{ServiceSid}/PhoneNumbers/{Sid}"),
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
    /// A Messaging Service resource to add, fetch or remove phone numbers from a Messaging Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to fetch the resource from.</param>
    /// <param name="sid">The SID of the PhoneNumber resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServicePhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServicePhoneNumber> FetchPhoneNumber(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{ServiceSid}/PhoneNumbers/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1ServicePhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to add, fetch or remove phone numbers from a Messaging Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to read the resources from.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListPhoneNumberResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListPhoneNumberResponse> ListPhoneNumber(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{ServiceSid}/PhoneNumbers"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPhoneNumberResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
