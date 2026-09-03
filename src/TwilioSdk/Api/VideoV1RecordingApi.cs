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

public sealed class VideoV1RecordingApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1RecordingApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a Recording resource identified by a Recording SID.
    /// </summary>
    /// <param name="sid">The SID of the Recording resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a Recording resource identified by a Recording SID.
    /// </remarks>
    public Task DeleteRecording2(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Recordings/{Sid}"),
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
    /// Returns a single Recording resource identified by a Recording SID.
    /// </summary>
    /// <param name="sid">The SID of the Recording resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Recording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a single Recording resource identified by a Recording SID.
    /// </remarks>
    public Task<VideoV1Recording> FetchRecording2(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Recordings/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1Recording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List of all Track recordings.
    /// </summary>
    /// <param name="status">Read only the recordings that have this status. Can be: <c>processing</c>, <c>completed</c>, or <c>deleted</c>.</param>
    /// <param name="sourceSid">Read only the recordings that have this <c>source_sid</c>.</param>
    /// <param name="groupingSid">Read only recordings with this <c>grouping_sid</c>, which may include a <c>participant_sid</c> and/or a <c>room_sid</c>.</param>
    /// <param name="dateCreatedAfter">Read only recordings that started on or after this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date-time with time zone.</param>
    /// <param name="dateCreatedBefore">Read only recordings that started before this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date-time with time zone, given as <c>YYYY-MM-DDThh:mm:ss+|-hh:mm</c> or <c>YYYY-MM-DDThh:mm:ssZ</c>.</param>
    /// <param name="mediaType">Read only recordings that have this media type. Can be either <c>audio</c> or <c>video</c>.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="pageSize">How many resources to return in each list page.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRecordingResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List of all Track recordings.
    /// </remarks>
    public Task<ListRecordingResponse1> ListRecording2(RecordingEnumStatus1? status,
        string? sourceSid,
        IReadOnlyList<string>? groupingSid,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        RecordingEnumType? mediaType,
        int? page,
        string? pageToken,
        long? pageSize = 50L,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Recordings"),
            [],
            [new Param("Status", status),
                new Param("SourceSid", sourceSid),
                new Param("GroupingSid", groupingSid),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("MediaType", mediaType),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRecordingResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
