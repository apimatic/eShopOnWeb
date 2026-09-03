using System;
using System.Collections.Generic;
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

public sealed class TaskrouterV1Task
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1Task(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceTask> CreateTask(string workspaceSid,
        int? timeout,
        int? priority,
        string? taskChannel,
        string? workflowSid,
        string? attributes,
        DateTimeOffset? virtualStartTime,
        string? routingTarget,
        string? ignoreCapacity,
        string? taskQueueSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Tasks"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Timeout", timeout),
                    new Param("Priority", priority),
                    new Param("TaskChannel", taskChannel),
                    new Param("WorkflowSid", workflowSid),
                    new Param("Attributes", attributes),
                    new Param("VirtualStartTime", virtualStartTime),
                    new Param("RoutingTarget", routingTarget),
                    new Param("IgnoreCapacity", ignoreCapacity),
                    new Param("TaskQueueSid", taskQueueSid)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTask>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task DeleteTask(string workspaceSid,
        string sid,
        string? ifMatch,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Tasks/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1WorkspaceTask> FetchTask(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Tasks/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceTask>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListTaskResponse> ListTask(string workspaceSid,
        int? priority,
        IReadOnlyList<string>? assignmentStatus,
        string? workflowSid,
        string? workflowName,
        string? taskQueueSid,
        string? taskQueueName,
        string? evaluateTaskAttributes,
        string? routingTarget,
        string? ordering,
        bool? hasAddons,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Tasks"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("Priority", priority),
                new Param("AssignmentStatus", assignmentStatus),
                new Param("WorkflowSid", workflowSid),
                new Param("WorkflowName", workflowName),
                new Param("TaskQueueSid", taskQueueSid),
                new Param("TaskQueueName", taskQueueName),
                new Param("EvaluateTaskAttributes", evaluateTaskAttributes),
                new Param("RoutingTarget", routingTarget),
                new Param("Ordering", ordering),
                new Param("HasAddons", hasAddons),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTaskResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1WorkspaceTask> UpdateTask(string workspaceSid,
        string sid,
        string? ifMatch,
        string? attributes,
        TaskEnumStatus? assignmentStatus,
        string? reason,
        int? priority,
        string? taskChannel,
        DateTimeOffset? virtualStartTime,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Tasks/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Attributes", attributes),
                    new Param("AssignmentStatus", assignmentStatus),
                    new Param("Reason", reason),
                    new Param("Priority", priority),
                    new Param("TaskChannel", taskChannel),
                    new Param("VirtualStartTime", virtualStartTime)]),
            JsonResponse.Create<TaskrouterV1WorkspaceTask>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
