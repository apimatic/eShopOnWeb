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

public sealed class TaskrouterV1WorkerChannel
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1WorkerChannel(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceWorkerWorkerChannel> FetchWorkerChannel(string workspaceSid,
        string workerSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Channels/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid),
                new TemplateParam("WorkerSid", workerSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceWorkerWorkerChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListWorkerChannelResponse> ListWorkerChannel(string workspaceSid,
        string workerSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Channels"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("WorkerSid", workerSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListWorkerChannelResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1WorkspaceWorkerWorkerChannel> UpdateWorkerChannel(string workspaceSid,
        string workerSid,
        string sid,
        int? capacity,
        bool? available,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Channels/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid),
                new TemplateParam("WorkerSid", workerSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Capacity", capacity), new Param("Available", available)]),
            JsonResponse.Create<TaskrouterV1WorkspaceWorkerWorkerChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
