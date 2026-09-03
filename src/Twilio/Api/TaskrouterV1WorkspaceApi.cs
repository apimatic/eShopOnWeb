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

public sealed class TaskrouterV1WorkspaceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1WorkspaceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1Workspace> CreateWorkspace(string friendlyName,
        string? eventCallbackUrl,
        string? eventsFilter,
        bool? multiTaskEnabled,
        string? template,
        WorkspaceEnumQueueOrder? prioritizeQueueOrder,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("EventCallbackUrl", eventCallbackUrl),
                    new Param("EventsFilter", eventsFilter),
                    new Param("MultiTaskEnabled", multiTaskEnabled),
                    new Param("Template", template),
                    new Param("PrioritizeQueueOrder", prioritizeQueueOrder)]),
            JsonResponse.Create<TaskrouterV1Workspace>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task DeleteWorkspace(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{Sid}"),
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

    public Task<TaskrouterV1Workspace> FetchWorkspace(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1Workspace>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListWorkspaceResponse> ListWorkspace(string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces"),
            [],
            [new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListWorkspaceResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1Workspace> UpdateWorkspace(string sid,
        string? defaultActivitySid,
        string? eventCallbackUrl,
        string? eventsFilter,
        string? friendlyName,
        bool? multiTaskEnabled,
        string? timeoutActivitySid,
        WorkspaceEnumQueueOrder? prioritizeQueueOrder,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DefaultActivitySid", defaultActivitySid),
                    new Param("EventCallbackUrl", eventCallbackUrl),
                    new Param("EventsFilter", eventsFilter),
                    new Param("FriendlyName", friendlyName),
                    new Param("MultiTaskEnabled", multiTaskEnabled),
                    new Param("TimeoutActivitySid", timeoutActivitySid),
                    new Param("PrioritizeQueueOrder", prioritizeQueueOrder)]),
            JsonResponse.Create<TaskrouterV1Workspace>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
