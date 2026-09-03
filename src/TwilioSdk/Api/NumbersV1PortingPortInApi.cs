using System;
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

public sealed class NumbersV1PortingPortInApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1PortingPortInApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Allows to create a new port in request
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1PortingPortIn"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Allows to create a new port in request
    /// </remarks>
    public Task<NumbersV1PortingPortIn> CreatePortingPortIn(PortInRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<NumbersV1PortingPortIn>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Allows to cancel a port in request by SID
    /// </summary>
    /// <param name="portInRequestSid">The SID of the Port In request. This is a unique identifier of the port in request.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Allows to cancel a port in request by SID
    /// </remarks>
    public Task DeletePortingPortIn(string portInRequestSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn/{PortInRequestSid}"),
            [new TemplateParam("PortInRequestSid", portInRequestSid)],
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
    /// Fetch a port in request by SID
    /// </summary>
    /// <param name="portInRequestSid">The SID of the Port In request. This is a unique identifier of the port in request.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1PortingPortIn"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a port in request by SID
    /// </remarks>
    public Task<NumbersV1PortingPortIn> FetchPortingPortIn(string portInRequestSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn/{PortInRequestSid}"),
            [new TemplateParam("PortInRequestSid", portInRequestSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV1PortingPortIn>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch all PortInRequests for a user
    /// </summary>
    /// <param name="token">Page start token, if null then it will start from the beginning</param>
    /// <param name="portInRequestSid">Filter by Port in request SID, supports multiple values separated by comma</param>
    /// <param name="portInRequestStatus">Filter by Port In request status</param>
    /// <param name="createdBefore">Find all created before a certain date</param>
    /// <param name="createdAfter">Find all created after a certain date</param>
    /// <param name="size">Number of items per page</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListPortInRequestsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all PortInRequests for a user
    /// </remarks>
    public Task<ListPortInRequestsResponse> ListPortInRequests(string? token,
        string? portInRequestSid,
        string? portInRequestStatus,
        string? createdBefore,
        string? createdAfter,
        int? size = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn/PortInRequests"),
            [],
            [new Param("Token", token),
                new Param("Size", size),
                new Param("PortInRequestSid", portInRequestSid),
                new Param("PortInRequestStatus", portInRequestStatus),
                new Param("CreatedBefore", createdBefore),
                new Param("CreatedAfter", createdAfter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPortInRequestsResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
