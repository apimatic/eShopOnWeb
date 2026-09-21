using System;
using System.Collections.Generic;
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

public sealed class VideoV1CompositionApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1CompositionApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Recording compositions
    /// </summary>
    /// <param name="roomSid"></param>
    /// <param name="videoLayout"></param>
    /// <param name="audioSources"></param>
    /// <param name="audioSourcesExcluded"></param>
    /// <param name="resolution"></param>
    /// <param name="format"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="trim"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Composition"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1Composition> CreateComposition(string roomSid,
        object? videoLayout,
        IReadOnlyList<string>? audioSources,
        IReadOnlyList<string>? audioSourcesExcluded,
        string? resolution,
        CompositionEnumFormat? format,
        string? statusCallback,
        AmdStatusCallbackMethod? statusCallbackMethod,
        bool? trim,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Compositions"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("RoomSid", roomSid),
                    new Param("VideoLayout", videoLayout),
                    new Param("AudioSources", audioSources),
                    new Param("AudioSourcesExcluded", audioSourcesExcluded),
                    new Param("Resolution", resolution),
                    new Param("Format", format),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("Trim", trim)]),
            JsonResponse.Create<VideoV1Composition>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a Recording Composition resource identified by a Composition SID.
    /// </summary>
    /// <param name="sid">The SID of the Composition resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a Recording Composition resource identified by a Composition SID.
    /// </remarks>
    public Task DeleteComposition(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Compositions/{Sid}"),
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
    /// Returns a single Composition resource identified by a Composition SID.
    /// </summary>
    /// <param name="sid">The SID of the Composition resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Composition"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a single Composition resource identified by a Composition SID.
    /// </remarks>
    public Task<VideoV1Composition> FetchComposition(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Compositions/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1Composition>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List of all Recording compositions.
    /// </summary>
    /// <param name="status">Read only Composition resources with this status. Can be: <c>enqueued</c>, <c>processing</c>, <c>completed</c>, <c>deleted</c>, or <c>failed</c>.</param>
    /// <param name="dateCreatedAfter">Read only Composition resources created on or after this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date-time with time zone.</param>
    /// <param name="dateCreatedBefore">Read only Composition resources created before this ISO 8601 date-time with time zone.</param>
    /// <param name="roomSid">Read only Composition resources with this Room SID.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="pageSize">How many resources to return in each list page.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCompositionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List of all Recording compositions.
    /// </remarks>
    public Task<ListCompositionResponse> ListComposition(CompositionEnumStatus? status,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        string? roomSid,
        int? page,
        string? pageToken,
        long? pageSize = 50L,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Compositions"),
            [],
            [new Param("Status", status),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("RoomSid", roomSid),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCompositionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
