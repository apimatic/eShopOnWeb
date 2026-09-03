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

public sealed class TaskrouterV1WorkspaceRealTimeStatistics
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1WorkspaceRealTimeStatistics(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<TaskrouterV1WorkspaceWorkspaceRealTimeStatistics> FetchWorkspaceRealTimeStatistics(string workspaceSid,
        string? taskChannel,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/RealTimeStatistics"),
            [new TemplateParam("WorkspaceSid", workspaceSid)],
            [new Param("TaskChannel", taskChannel)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceWorkspaceRealTimeStatistics>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
