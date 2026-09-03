using System;
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

public sealed class MessagingV1ServiceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1ServiceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// A Messaging Service resource to create, fetch, update, delete or add/remove senders from Messaging Services.
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="inboundRequestUrl"></param>
    /// <param name="inboundMethod"></param>
    /// <param name="fallbackUrl"></param>
    /// <param name="fallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="stickySender"></param>
    /// <param name="mmsConverter"></param>
    /// <param name="smartEncoding"></param>
    /// <param name="scanMessageContent"></param>
    /// <param name="fallbackToLongCode"></param>
    /// <param name="areaCodeGeomatch"></param>
    /// <param name="validityPeriod"></param>
    /// <param name="synchronousValidation"></param>
    /// <param name="usecase"></param>
    /// <param name="useInboundWebhookOnNumber"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1Service> CreateService(string friendlyName,
        string? inboundRequestUrl,
        AmdStatusCallbackMethod? inboundMethod,
        string? fallbackUrl,
        AmdStatusCallbackMethod? fallbackMethod,
        string? statusCallback,
        bool? stickySender,
        bool? mmsConverter,
        bool? smartEncoding,
        ServiceEnumScanMessageContent? scanMessageContent,
        bool? fallbackToLongCode,
        bool? areaCodeGeomatch,
        int? validityPeriod,
        bool? synchronousValidation,
        string? usecase,
        bool? useInboundWebhookOnNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("InboundRequestUrl", inboundRequestUrl),
                    new Param("InboundMethod", inboundMethod),
                    new Param("FallbackUrl", fallbackUrl),
                    new Param("FallbackMethod", fallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StickySender", stickySender),
                    new Param("MmsConverter", mmsConverter),
                    new Param("SmartEncoding", smartEncoding),
                    new Param("ScanMessageContent", scanMessageContent),
                    new Param("FallbackToLongCode", fallbackToLongCode),
                    new Param("AreaCodeGeomatch", areaCodeGeomatch),
                    new Param("ValidityPeriod", validityPeriod),
                    new Param("SynchronousValidation", synchronousValidation),
                    new Param("Usecase", usecase),
                    new Param("UseInboundWebhookOnNumber", useInboundWebhookOnNumber)]),
            JsonResponse.Create<MessagingV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to create, fetch, update, delete or add/remove senders from Messaging Services.
    /// </summary>
    /// <param name="sid">The SID of the Service resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteService(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{Sid}"),
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
    /// A Messaging Service resource to create, fetch, update, delete or add/remove senders from Messaging Services.
    /// </summary>
    /// <param name="sid">The SID of the Service resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1Service> FetchService(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to create, fetch, update, delete or add/remove senders from Messaging Services.
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListServiceResponse> ListService(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to create, fetch, update, delete or add/remove senders from Messaging Services.
    /// </summary>
    /// <param name="sid">The SID of the Service resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="inboundRequestUrl"></param>
    /// <param name="inboundMethod"></param>
    /// <param name="fallbackUrl"></param>
    /// <param name="fallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="stickySender"></param>
    /// <param name="mmsConverter"></param>
    /// <param name="smartEncoding"></param>
    /// <param name="scanMessageContent"></param>
    /// <param name="fallbackToLongCode"></param>
    /// <param name="areaCodeGeomatch"></param>
    /// <param name="validityPeriod"></param>
    /// <param name="synchronousValidation"></param>
    /// <param name="usecase"></param>
    /// <param name="useInboundWebhookOnNumber"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1Service> UpdateService(string sid,
        string? friendlyName,
        string? inboundRequestUrl,
        AmdStatusCallbackMethod? inboundMethod,
        string? fallbackUrl,
        AmdStatusCallbackMethod? fallbackMethod,
        string? statusCallback,
        bool? stickySender,
        bool? mmsConverter,
        bool? smartEncoding,
        ServiceEnumScanMessageContent? scanMessageContent,
        bool? fallbackToLongCode,
        bool? areaCodeGeomatch,
        int? validityPeriod,
        bool? synchronousValidation,
        string? usecase,
        bool? useInboundWebhookOnNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("InboundRequestUrl", inboundRequestUrl),
                    new Param("InboundMethod", inboundMethod),
                    new Param("FallbackUrl", fallbackUrl),
                    new Param("FallbackMethod", fallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StickySender", stickySender),
                    new Param("MmsConverter", mmsConverter),
                    new Param("SmartEncoding", smartEncoding),
                    new Param("ScanMessageContent", scanMessageContent),
                    new Param("FallbackToLongCode", fallbackToLongCode),
                    new Param("AreaCodeGeomatch", areaCodeGeomatch),
                    new Param("ValidityPeriod", validityPeriod),
                    new Param("SynchronousValidation", synchronousValidation),
                    new Param("Usecase", usecase),
                    new Param("UseInboundWebhookOnNumber", useInboundWebhookOnNumber)]),
            JsonResponse.Create<MessagingV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
