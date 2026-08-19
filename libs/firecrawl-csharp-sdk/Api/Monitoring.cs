using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Api;

public sealed class Monitoring
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Monitoring(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a monitor
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateMonitorError"/> when the server returns an error response.</exception>
    public Task<MonitorResponse> CreateMonitor(MonitorCreateRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<MonitorResponse>(),
            CreateMonitorErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a monitor
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SuccessResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteMonitorError"/> when the server returns an error response.</exception>
    public Task<SuccessResponse> DeleteMonitor(Guid monitorId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}"),
            [new TemplateParam("monitorId", monitorId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<SuccessResponse>(),
            DeleteMonitorErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get a monitor
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetMonitorError"/> when the server returns an error response.</exception>
    public Task<MonitorResponse> GetMonitor(Guid monitorId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}"),
            [new TemplateParam("monitorId", monitorId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MonitorResponse>(),
            GetMonitorErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get a monitor check
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="checkId">The monitor check ID</param>
    /// <param name="status"></param>
    /// <param name="limit"></param>
    /// <param name="skip">Number of page results to skip. Use the <c>next</c> URL from the previous response for pagination.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorCheckDetailResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetMonitorCheckError"/> when the server returns an error response.</exception>
    public Task<MonitorCheckDetailResponse> GetMonitorCheck(Guid monitorId,
        Guid checkId,
        Status3? status,
        int? limit = 25,
        int? skip = 0,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}/checks/{checkId}"),
            [new TemplateParam("monitorId", monitorId), new TemplateParam("checkId", checkId)],
            [new Param("limit", limit), new Param("skip", skip), new Param("status", status)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MonitorCheckDetailResponse>(),
            GetMonitorCheckErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// List monitor checks
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="status">Filter checks by status.</param>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorCheckListResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MonitorCheckListResponse> ListMonitorChecks(Guid monitorId,
        Status2? status,
        int? limit = 25,
        int? offset = 0,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}/checks"),
            [new TemplateParam("monitorId", monitorId)],
            [new Param("limit", limit), new Param("offset", offset), new Param("status", status)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MonitorCheckListResponse>(),
            RawErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// List monitors
    /// </summary>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorListResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MonitorListResponse> ListMonitors(int? limit = 25,
        int? offset = 0,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor"),
            [],
            [new Param("limit", limit), new Param("offset", offset)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MonitorListResponse>(),
            RawErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Run a monitor
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorRunResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RunMonitorError"/> when the server returns an error response.</exception>
    public Task<MonitorRunResponse> RunMonitor(Guid monitorId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}/run"),
            [new TemplateParam("monitorId", monitorId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<MonitorRunResponse>(),
            RunMonitorErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Update a monitor
    /// </summary>
    /// <param name="monitorId">The monitor ID</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MonitorResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateMonitorError"/> when the server returns an error response.</exception>
    public Task<MonitorResponse> UpdateMonitor(Guid monitorId,
        MonitorUpdateRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/monitor/{monitorId}"),
            [new TemplateParam("monitorId", monitorId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            JsonResponse.Create<MonitorResponse>(),
            UpdateMonitorErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}
