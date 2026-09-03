using System;
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

public sealed class V2ShortCodeApplications
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal V2ShortCodeApplications(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new short code application
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CreateShortCodeApplicationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new short code application for an account
    /// </remarks>
    public Task<CreateShortCodeApplicationResponse> CreateShortCodeApplication(CreateShortCodeApplicationRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/ShortCodes/Applications"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CreateShortCodeApplicationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific Short Code Application instance.
    /// </summary>
    /// <param name="sid">The unique string that identifies the Short Code Application resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ShortCodeApplication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Short Code Application instance.
    /// </remarks>
    public Task<ShortCodeApplication> FetchShortCodeApplication(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/ShortCodes/Applications/{sid}"),
            [new TemplateParam("sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ShortCodeApplication>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List Short Code Applications
    /// </summary>
    /// <param name="accountSid">The Account SID to filter by.</param>
    /// <param name="isoCountry">The ISO country to filter by.</param>
    /// <param name="status">The application status to filter by.</param>
    /// <param name="friendlyName">The friendly name to filter by.</param>
    /// <param name="sid">The application SID to filter by.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The current page.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ShortCodeApplicationResponsePage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// list of all short code applications for an account
    /// </remarks>
    public Task<ShortCodeApplicationResponsePage> ListShortCodeApplications(string? accountSid,
        string? isoCountry,
        string? status,
        string? friendlyName,
        string? sid,
        int? pageSize,
        int? page = 0,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/ShortCodes/Applications"),
            [],
            [new Param("AccountSid", accountSid),
                new Param("IsoCountry", isoCountry),
                new Param("Status", status),
                new Param("FriendlyName", friendlyName),
                new Param("Sid", sid),
                new Param("PageSize", pageSize),
                new Param("Page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ShortCodeApplicationResponsePage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
