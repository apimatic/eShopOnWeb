using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Extensions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;

namespace Twilio.Api;

public sealed class TaskrouterV1Event
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1Event(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceEvent> FetchEvent(string workspaceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Events/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceEvent>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListEventResponse> ListEvent(string workspaceSid,
        DateTimeOffset? endDate,
        string? eventType,
        int? minutes,
        string? reservationSid,
        DateTimeOffset? startDate,
        string? taskQueueSid,
        string? taskSid,
        string? workerSid,
        string? workflowSid,
        string? taskChannel,
        string? sid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Events"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("EndDate", endDate?.ToIso8601()),
                new Param("EventType", eventType),
                new Param("Minutes", minutes),
                new Param("ReservationSid", reservationSid),
                new Param("StartDate", startDate?.ToIso8601()),
                new Param("TaskQueueSid", taskQueueSid),
                new Param("TaskSid", taskSid),
                new Param("WorkerSid", workerSid),
                new Param("WorkflowSid", workflowSid),
                new Param("TaskChannel", taskChannel),
                new Param("Sid", sid),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListEventResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
