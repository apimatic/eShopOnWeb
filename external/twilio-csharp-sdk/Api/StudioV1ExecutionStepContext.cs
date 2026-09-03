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

public sealed class StudioV1ExecutionStepContext
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV1ExecutionStepContext(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve the context for an Execution Step.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Step to fetch.</param>
    /// <param name="executionSid">The SID of the Execution resource with the Step to fetch.</param>
    /// <param name="stepSid">The SID of the Step to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV1FlowExecutionExecutionStepExecutionStepContext"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the context for an Execution Step.
    /// </remarks>
    public Task<StudioV1FlowExecutionExecutionStepExecutionStepContext> FetchExecutionStepContext(string flowSid,
        string executionSid,
        string stepSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v1/Flows/{FlowSid}/Executions/{ExecutionSid}/Steps/{StepSid}/Context"),
            [new TemplateParam("FlowSid", flowSid),
                new TemplateParam("ExecutionSid", executionSid),
                new TemplateParam("StepSid", stepSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV1FlowExecutionExecutionStepExecutionStepContext>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
