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

public sealed class TaskrouterV1TaskChannel
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1TaskChannel(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Types of tasks
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace that the new Task Channel belongs to.</param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="channelOptimizedRouting"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TaskrouterV1WorkspaceTaskChannel"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<TaskrouterV1WorkspaceTaskChannel> CreateTaskChannel(string workspaceSid,
        string friendlyName,
        string uniqueName,
        bool? channelOptimizedRouting,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskChannels"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("UniqueName", uniqueName),
                    new Param("ChannelOptimizedRouting", channelOptimizedRouting)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTaskChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Types of tasks
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the Task Channel to delete.</param>
    /// <param name="sid">The SID of the Task Channel resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteTaskChannel(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskChannels/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
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
    /// Types of tasks
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the Task Channel to fetch.</param>
    /// <param name="sid">The SID of the Task Channel resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TaskrouterV1WorkspaceTaskChannel"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<TaskrouterV1WorkspaceTaskChannel> FetchTaskChannel(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskChannels/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceTaskChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Types of tasks
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the Task Channel to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListTaskChannelResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListTaskChannelResponse> ListTaskChannel(string workspaceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskChannels"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTaskChannelResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Types of tasks
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the Task Channel to update.</param>
    /// <param name="sid">The SID of the Task Channel resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="channelOptimizedRouting"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TaskrouterV1WorkspaceTaskChannel"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<TaskrouterV1WorkspaceTaskChannel> UpdateTaskChannel(string workspaceSid,
        string sid,
        string? friendlyName,
        bool? channelOptimizedRouting,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskChannels/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("ChannelOptimizedRouting", channelOptimizedRouting)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTaskChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
