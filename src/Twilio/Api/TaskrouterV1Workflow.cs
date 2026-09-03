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

public sealed class TaskrouterV1Workflow
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1Workflow(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceWorkflow> CreateWorkflow(string workspaceSid,
        string friendlyName,
        string configuration,
        string? assignmentCallbackUrl,
        string? fallbackAssignmentCallbackUrl,
        int? taskReservationTimeout,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workflows"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Configuration", configuration),
                    new Param("AssignmentCallbackUrl", assignmentCallbackUrl),
                    new Param("FallbackAssignmentCallbackUrl", fallbackAssignmentCallbackUrl),
                    new Param("TaskReservationTimeout", taskReservationTimeout)]),
            JsonResponse.Create<TaskrouterV1WorkspaceWorkflow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task DeleteWorkflow(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workflows/{Sid}"),
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

    public Task<TaskrouterV1WorkspaceWorkflow> FetchWorkflow(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workflows/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceWorkflow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListWorkflowResponse> ListWorkflow(string workspaceSid,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workflows"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListWorkflowResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<TaskrouterV1WorkspaceWorkflow> UpdateWorkflow(string workspaceSid,
        string sid,
        string? friendlyName,
        string? assignmentCallbackUrl,
        string? fallbackAssignmentCallbackUrl,
        string? configuration,
        int? taskReservationTimeout,
        string? reEvaluateTasks,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workflows/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("AssignmentCallbackUrl", assignmentCallbackUrl),
                    new Param("FallbackAssignmentCallbackUrl", fallbackAssignmentCallbackUrl),
                    new Param("Configuration", configuration),
                    new Param("TaskReservationTimeout", taskReservationTimeout),
                    new Param("ReEvaluateTasks", reEvaluateTasks)]),
            JsonResponse.Create<TaskrouterV1WorkspaceWorkflow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
