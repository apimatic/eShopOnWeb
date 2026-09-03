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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class ProxyV1ServiceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProxyV1ServiceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Service for Twilio Proxy
    /// </summary>
    /// <param name="uniqueName"></param>
    /// <param name="defaultTtl"></param>
    /// <param name="callbackUrl"></param>
    /// <param name="geoMatchLevel"></param>
    /// <param name="numberSelectionBehavior"></param>
    /// <param name="interceptCallbackUrl"></param>
    /// <param name="outOfSessionCallbackUrl"></param>
    /// <param name="chatInstanceSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Service for Twilio Proxy
    /// </remarks>
    public Task<ProxyV1Service> CreateService4(string uniqueName,
        int? defaultTtl,
        string? callbackUrl,
        ServiceEnumGeoMatchLevel? geoMatchLevel,
        ServiceEnumNumberSelectionBehavior? numberSelectionBehavior,
        string? interceptCallbackUrl,
        string? outOfSessionCallbackUrl,
        string? chatInstanceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("UniqueName", uniqueName),
                    new Param("DefaultTtl", defaultTtl),
                    new Param("CallbackUrl", callbackUrl),
                    new Param("GeoMatchLevel", geoMatchLevel),
                    new Param("NumberSelectionBehavior", numberSelectionBehavior),
                    new Param("InterceptCallbackUrl", interceptCallbackUrl),
                    new Param("OutOfSessionCallbackUrl", outOfSessionCallbackUrl),
                    new Param("ChatInstanceSid", chatInstanceSid)]),
            JsonResponse.Create<ProxyV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Service.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Service resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Service.
    /// </remarks>
    public Task DeleteService4(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Fetch a specific Service.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Service resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Service.
    /// </remarks>
    public Task<ProxyV1Service> FetchService4(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProxyV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Services for Twilio Proxy. A maximum of 100 records will be returned per page.
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceResponse3"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Services for Twilio Proxy. A maximum of 100 records will be returned per page.
    /// </remarks>
    public Task<ListServiceResponse3> ListService4(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceResponse3>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Service.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Service resource to update.</param>
    /// <param name="uniqueName"></param>
    /// <param name="defaultTtl"></param>
    /// <param name="callbackUrl"></param>
    /// <param name="geoMatchLevel"></param>
    /// <param name="numberSelectionBehavior"></param>
    /// <param name="interceptCallbackUrl"></param>
    /// <param name="outOfSessionCallbackUrl"></param>
    /// <param name="chatInstanceSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Service.
    /// </remarks>
    public Task<ProxyV1Service> UpdateService3(string sid,
        string? uniqueName,
        int? defaultTtl,
        string? callbackUrl,
        ServiceEnumGeoMatchLevel? geoMatchLevel,
        ServiceEnumNumberSelectionBehavior? numberSelectionBehavior,
        string? interceptCallbackUrl,
        string? outOfSessionCallbackUrl,
        string? chatInstanceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("UniqueName", uniqueName),
                    new Param("DefaultTtl", defaultTtl),
                    new Param("CallbackUrl", callbackUrl),
                    new Param("GeoMatchLevel", geoMatchLevel),
                    new Param("NumberSelectionBehavior", numberSelectionBehavior),
                    new Param("InterceptCallbackUrl", interceptCallbackUrl),
                    new Param("OutOfSessionCallbackUrl", outOfSessionCallbackUrl),
                    new Param("ChatInstanceSid", chatInstanceSid)]),
            JsonResponse.Create<ProxyV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
