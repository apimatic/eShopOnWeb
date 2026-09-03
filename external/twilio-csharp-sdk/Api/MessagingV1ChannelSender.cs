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

namespace Twilio.Api;

public sealed class MessagingV1ChannelSender
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1ChannelSender(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// A Messaging Service resource to read, fetch all Channel Senders associated with a Messaging Service.
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to create the resource under.</param>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceChannelSender"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceChannelSender> CreateChannelSender(string messagingServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/ChannelSenders"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Sid", sid)]),
            JsonResponse.Create<MessagingV1ServiceChannelSender>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to read, fetch all Channel Senders associated with a Messaging Service.
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to delete the resource from.</param>
    /// <param name="sid">The SID of the Channel Sender resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteChannelSender(string messagingServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/ChannelSenders/{Sid}"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid), new TemplateParam("Sid", sid)],
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
    /// A Messaging Service resource to read, fetch all Channel Senders associated with a Messaging Service.
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to fetch the resource from.</param>
    /// <param name="sid">The SID of the ChannelSender resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceChannelSender"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceChannelSender> FetchChannelSender(string messagingServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/ChannelSenders/{Sid}"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1ServiceChannelSender>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to read, fetch all Channel Senders associated with a Messaging Service.
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> to read the resources from.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListChannelSenderResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListChannelSenderResponse> ListChannelSender(string messagingServiceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/ChannelSenders"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListChannelSenderResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
