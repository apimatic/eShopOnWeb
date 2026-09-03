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

public sealed class TaskrouterV1TaskQueueRealTimeStatistics
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1TaskQueueRealTimeStatistics(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceTaskQueueTaskQueueRealTimeStatistics> FetchTaskQueueRealTimeStatistics(string workspaceSid,
        string taskQueueSid,
        string? taskChannel,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/TaskQueues/{TaskQueueSid}/RealTimeStatistics"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("TaskQueueSid", taskQueueSid)],
            [new Param("TaskChannel", taskChannel)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceTaskQueueTaskQueueRealTimeStatistics>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
