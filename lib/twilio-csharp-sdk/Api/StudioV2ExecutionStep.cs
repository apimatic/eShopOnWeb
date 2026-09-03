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

public sealed class StudioV2ExecutionStep
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV2ExecutionStep(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve a Step.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Step to fetch.</param>
    /// <param name="executionSid">The SID of the Execution resource with the Step to fetch.</param>
    /// <param name="sid">The SID of the ExecutionStep resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV1FlowExecutionExecutionStep"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a Step.
    /// </remarks>
    public Task<StudioV1FlowExecutionExecutionStep> FetchExecutionStep2(string flowSid,
        string executionSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{ExecutionSid}/Steps/{Sid}"),
            [new TemplateParam("FlowSid", flowSid),
                new TemplateParam("ExecutionSid", executionSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV1FlowExecutionExecutionStep>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Steps for an Execution.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Steps to read.</param>
    /// <param name="executionSid">The SID of the Execution with the Steps to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListExecutionStepResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Steps for an Execution.
    /// </remarks>
    public Task<ListExecutionStepResponse> ListExecutionStep2(string flowSid,
        string executionSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{ExecutionSid}/Steps"),
            [new TemplateParam("FlowSid", flowSid), new TemplateParam("ExecutionSid", executionSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListExecutionStepResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
