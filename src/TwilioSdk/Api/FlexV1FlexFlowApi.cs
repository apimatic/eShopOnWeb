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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class FlexV1FlexFlowApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1FlexFlowApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Flex Flow
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="chatServiceSid"></param>
    /// <param name="channelType"></param>
    /// <param name="contactIdentity"></param>
    /// <param name="enabled"></param>
    /// <param name="integrationType"></param>
    /// <param name="integrationFlowSid"></param>
    /// <param name="integrationUrl"></param>
    /// <param name="integrationWorkspaceSid"></param>
    /// <param name="integrationWorkflowSid"></param>
    /// <param name="integrationChannel"></param>
    /// <param name="integrationTimeout"></param>
    /// <param name="integrationPriority"></param>
    /// <param name="integrationCreationOnMessage"></param>
    /// <param name="longLived"></param>
    /// <param name="janitorEnabled"></param>
    /// <param name="integrationRetryCount"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1FlexFlow"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<FlexV1FlexFlow> CreateFlexFlow(string friendlyName,
        string chatServiceSid,
        FlexFlowEnumChannelType channelType,
        string? contactIdentity,
        bool? enabled,
        FlexFlowEnumIntegrationType? integrationType,
        string? integrationFlowSid,
        string? integrationUrl,
        string? integrationWorkspaceSid,
        string? integrationWorkflowSid,
        string? integrationChannel,
        int? integrationTimeout,
        int? integrationPriority,
        bool? integrationCreationOnMessage,
        bool? longLived,
        bool? janitorEnabled,
        int? integrationRetryCount,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/FlexFlows"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("ChatServiceSid", chatServiceSid),
                    new Param("ChannelType", channelType),
                    new Param("ContactIdentity", contactIdentity),
                    new Param("Enabled", enabled),
                    new Param("IntegrationType", integrationType),
                    new Param("Integration.FlowSid", integrationFlowSid),
                    new Param("Integration.Url", integrationUrl),
                    new Param("Integration.WorkspaceSid", integrationWorkspaceSid),
                    new Param("Integration.WorkflowSid", integrationWorkflowSid),
                    new Param("Integration.Channel", integrationChannel),
                    new Param("Integration.Timeout", integrationTimeout),
                    new Param("Integration.Priority", integrationPriority),
                    new Param("Integration.CreationOnMessage", integrationCreationOnMessage),
                    new Param("LongLived", longLived),
                    new Param("JanitorEnabled", janitorEnabled),
                    new Param("Integration.RetryCount", integrationRetryCount)]),
            JsonResponse.Create<FlexV1FlexFlow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Flex Flow
    /// </summary>
    /// <param name="sid">The SID of the Flex Flow resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteFlexFlow(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/FlexFlows/{Sid}"),
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
    /// Flex Flow
    /// </summary>
    /// <param name="sid">The SID of the Flex Flow resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1FlexFlow"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<FlexV1FlexFlow> FetchFlexFlow(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/FlexFlows/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1FlexFlow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Flex Flow
    /// </summary>
    /// <param name="friendlyName">The <c>friendly_name</c> of the Flex Flow resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListFlexFlowResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListFlexFlowResponse> ListFlexFlow(string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/FlexFlows"),
            [],
            [new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListFlexFlowResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Flex Flow
    /// </summary>
    /// <param name="sid">The SID of the Flex Flow resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="chatServiceSid"></param>
    /// <param name="channelType"></param>
    /// <param name="contactIdentity"></param>
    /// <param name="enabled"></param>
    /// <param name="integrationType"></param>
    /// <param name="integrationFlowSid"></param>
    /// <param name="integrationUrl"></param>
    /// <param name="integrationWorkspaceSid"></param>
    /// <param name="integrationWorkflowSid"></param>
    /// <param name="integrationChannel"></param>
    /// <param name="integrationTimeout"></param>
    /// <param name="integrationPriority"></param>
    /// <param name="integrationCreationOnMessage"></param>
    /// <param name="longLived"></param>
    /// <param name="janitorEnabled"></param>
    /// <param name="integrationRetryCount"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1FlexFlow"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<FlexV1FlexFlow> UpdateFlexFlow(string sid,
        string? friendlyName,
        string? chatServiceSid,
        FlexFlowEnumChannelType? channelType,
        string? contactIdentity,
        bool? enabled,
        FlexFlowEnumIntegrationType? integrationType,
        string? integrationFlowSid,
        string? integrationUrl,
        string? integrationWorkspaceSid,
        string? integrationWorkflowSid,
        string? integrationChannel,
        int? integrationTimeout,
        int? integrationPriority,
        bool? integrationCreationOnMessage,
        bool? longLived,
        bool? janitorEnabled,
        int? integrationRetryCount,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/FlexFlows/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("ChatServiceSid", chatServiceSid),
                    new Param("ChannelType", channelType),
                    new Param("ContactIdentity", contactIdentity),
                    new Param("Enabled", enabled),
                    new Param("IntegrationType", integrationType),
                    new Param("Integration.FlowSid", integrationFlowSid),
                    new Param("Integration.Url", integrationUrl),
                    new Param("Integration.WorkspaceSid", integrationWorkspaceSid),
                    new Param("Integration.WorkflowSid", integrationWorkflowSid),
                    new Param("Integration.Channel", integrationChannel),
                    new Param("Integration.Timeout", integrationTimeout),
                    new Param("Integration.Priority", integrationPriority),
                    new Param("Integration.CreationOnMessage", integrationCreationOnMessage),
                    new Param("LongLived", longLived),
                    new Param("JanitorEnabled", janitorEnabled),
                    new Param("Integration.RetryCount", integrationRetryCount)]),
            JsonResponse.Create<FlexV1FlexFlow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
