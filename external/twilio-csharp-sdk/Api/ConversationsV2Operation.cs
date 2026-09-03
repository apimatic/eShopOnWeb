using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Errors;
using Twilio.Models;

namespace Twilio.Api;

/// <summary>
/// Poll the status of a long-running operation.
/// </summary>
public sealed class ConversationsV2Operation
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2Operation(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get Operation Status
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2OperationStatus"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchOperationStatusError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the current status of a long-running operation.
    /// Operations progress through: PENDING -&gt; RUNNING -&gt; COMPLETED or FAILED.
    /// </remarks>
    public Task<ConversationsV2OperationStatus> FetchOperationStatus(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Operations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2OperationStatus>(),
            FetchOperationStatusErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
