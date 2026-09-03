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

public sealed class TaskrouterV1TaskQueuesStatistics
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1TaskQueuesStatistics(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<ListTaskQueuesStatisticsResponse> ListTaskQueuesStatistics(string workspaceSid,
        DateTimeOffset? endDate,
        string? friendlyName,
        int? minutes,
        DateTimeOffset? startDate,
        string? taskChannel,
        string? splitByWaitTime,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/Statistics"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("EndDate", endDate?.ToIso8601()),
                new Param("FriendlyName", friendlyName),
                new Param("Minutes", minutes),
                new Param("StartDate", startDate?.ToIso8601()),
                new Param("TaskChannel", taskChannel),
                new Param("SplitByWaitTime", splitByWaitTime),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTaskQueuesStatisticsResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
