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

public sealed class TaskrouterV1TaskQueue
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1TaskQueue(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceTaskQueue> CreateTaskQueue(string workspaceSid,
        string friendlyName,
        string? targetWorkers,
        int? maxReservedWorkers,
        TaskQueueEnumTaskOrder? taskOrder,
        string? reservationActivitySid,
        string? assignmentActivitySid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("TargetWorkers", targetWorkers),
                    new Param("MaxReservedWorkers", maxReservedWorkers),
                    new Param("TaskOrder", taskOrder),
                    new Param("ReservationActivitySid", reservationActivitySid),
                    new Param("AssignmentActivitySid", assignmentActivitySid)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTaskQueue>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task DeleteTaskQueue(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/{Sid}"),
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

    public Task<TaskrouterV1WorkspaceTaskQueue> FetchTaskQueue(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceTaskQueue>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListTaskQueueResponse> ListTaskQueue(string workspaceSid,
        string? friendlyName,
        string? evaluateWorkerAttributes,
        string? workerSid,
        string? ordering,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("FriendlyName", friendlyName),
                new Param("EvaluateWorkerAttributes", evaluateWorkerAttributes),
                new Param("WorkerSid", workerSid),
                new Param("Ordering", ordering),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTaskQueueResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1WorkspaceTaskQueue> UpdateTaskQueue(string workspaceSid,
        string sid,
        string? friendlyName,
        string? targetWorkers,
        string? reservationActivitySid,
        string? assignmentActivitySid,
        int? maxReservedWorkers,
        TaskQueueEnumTaskOrder? taskOrder,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("TargetWorkers", targetWorkers),
                    new Param("ReservationActivitySid", reservationActivitySid),
                    new Param("AssignmentActivitySid", assignmentActivitySid),
                    new Param("MaxReservedWorkers", maxReservedWorkers),
                    new Param("TaskOrder", taskOrder)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTaskQueue>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
