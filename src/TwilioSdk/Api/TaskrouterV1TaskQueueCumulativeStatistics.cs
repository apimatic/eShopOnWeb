using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

public sealed class TaskrouterV1TaskQueueCumulativeStatistics
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1TaskQueueCumulativeStatistics(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceTaskQueueTaskQueueCumulativeStatistics> FetchTaskQueueCumulativeStatistics(string workspaceSid,
        string taskQueueSid,
        DateTimeOffset? endDate,
        int? minutes,
        DateTimeOffset? startDate,
        string? taskChannel,
        string? splitByWaitTime,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/{TaskQueueSid}/CumulativeStatistics"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("TaskQueueSid", taskQueueSid)],
            [new Param("EndDate", endDate?.ToIso8601()),
                new Param("Minutes", minutes),
                new Param("StartDate", startDate?.ToIso8601()),
                new Param("TaskChannel", taskChannel),
                new Param("SplitByWaitTime", splitByWaitTime)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceTaskQueueTaskQueueCumulativeStatistics>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
