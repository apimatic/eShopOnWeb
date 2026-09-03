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

public sealed class StudioV2ExecutionContext
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV2ExecutionContext(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve the most recent context for an Execution.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Execution context to fetch.</param>
    /// <param name="executionSid">The SID of the Execution context to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV1FlowExecutionExecutionContext"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the most recent context for an Execution.
    /// </remarks>
    public Task<StudioV1FlowExecutionExecutionContext> FetchExecutionContext2(string flowSid,
        string executionSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{ExecutionSid}/Context"),
            [new TemplateParam("FlowSid", flowSid), new TemplateParam("ExecutionSid", executionSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV1FlowExecutionExecutionContext>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
