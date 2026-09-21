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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class StudioV2Execution
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV2Execution(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Triggers a new Execution for the Flow
    /// </summary>
    /// <param name="flowSid">The SID of the Excecution's Flow.</param>
    /// <param name="to"></param>
    /// <param name="from"></param>
    /// <param name="parameters"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV2FlowExecution"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Triggers a new Execution for the Flow
    /// </remarks>
    public Task<StudioV2FlowExecution> CreateExecution2(string flowSid,
        string to,
        string from,
        object? parameters,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions"),
            [new TemplateParam("FlowSid", flowSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("To", to),
                    new Param("From", from),
                    new Param("Parameters", parameters)]),
            JsonResponse.Create<StudioV2FlowExecution>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete the Execution and all Steps relating to it.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Execution resources to delete.</param>
    /// <param name="sid">The SID of the Execution resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete the Execution and all Steps relating to it.
    /// </remarks>
    public Task DeleteExecution2(string flowSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{Sid}"),
            [new TemplateParam("FlowSid", flowSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve an Execution
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Execution resource to fetch</param>
    /// <param name="sid">The SID of the Execution resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV2FlowExecution"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve an Execution
    /// </remarks>
    public Task<StudioV2FlowExecution> FetchExecution2(string flowSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{Sid}"),
            [new TemplateParam("FlowSid", flowSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV2FlowExecution>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Executions for the Flow.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Execution resources to read.</param>
    /// <param name="status">Only show Execution resources with the given status. Can be: <c>active</c> or <c>ended</c>.</param>
    /// <param name="dateCreatedFrom">Only show Execution resources starting on or after this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date-time, given as <c>YYYY-MM-DDThh:mm:ss-hh:mm</c>.</param>
    /// <param name="dateCreatedTo">Only show Execution resources starting before this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date-time, given as <c>YYYY-MM-DDThh:mm:ss-hh:mm</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListExecutionResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Executions for the Flow.
    /// </remarks>
    public Task<ListExecutionResponse1> ListExecution2(string flowSid,
        EngagementEnumStatus? status,
        DateTimeOffset? dateCreatedFrom,
        DateTimeOffset? dateCreatedTo,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions"),
            [new TemplateParam("FlowSid", flowSid)],
            [new Param("status", status),
                new Param("DateCreatedFrom", dateCreatedFrom?.ToIso8601()),
                new Param("DateCreatedTo", dateCreatedTo?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListExecutionResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the status of an Execution to <c>ended</c>.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Execution resources to update.</param>
    /// <param name="sid">The SID of the Execution resource to update.</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV2FlowExecution"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the status of an Execution to <c>ended</c>.
    /// </remarks>
    public Task<StudioV2FlowExecution> UpdateExecution2(string flowSid,
        string sid,
        EngagementEnumStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v2/Flows/{FlowSid}/Executions/{Sid}"),
            [new TemplateParam("FlowSid", flowSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<StudioV2FlowExecution>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
