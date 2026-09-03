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

public sealed class TaskrouterV1WorkersStatistics
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1WorkersStatistics(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceWorkerWorkerStatistics> FetchWorkerStatistics(string workspaceSid,
        int? minutes,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        string? taskQueueSid,
        string? taskQueueName,
        string? friendlyName,
        string? taskChannel,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/Statistics"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("Minutes", minutes),
                new Param("StartDate", startDate?.ToIso8601()),
                new Param("EndDate", endDate?.ToIso8601()),
                new Param("TaskQueueSid", taskQueueSid),
                new Param("TaskQueueName", taskQueueName),
                new Param("FriendlyName", friendlyName),
                new Param("TaskChannel", taskChannel)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceWorkerWorkerStatistics>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
