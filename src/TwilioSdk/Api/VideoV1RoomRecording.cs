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

public sealed class VideoV1RoomRecording
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1RoomRecording(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Single-track, single-media room recordings
    /// </summary>
    /// <param name="roomSid">The SID of the room with the RoomRecording resource to delete.</param>
    /// <param name="sid">The SID of the RoomRecording resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteRoomRecording(string roomSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Recordings/{Sid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Sid", sid)],
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
    /// Single-track, single-media room recordings
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource with the recording to fetch.</param>
    /// <param name="sid">The SID of the RoomRecording resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomRecording> FetchRoomRecording(string roomSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Recordings/{Sid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Single-track, single-media room recordings
    /// </summary>
    /// <param name="roomSid">The SID of the room with the RoomRecording resources to read.</param>
    /// <param name="status">Read only the recordings with this status. Can be: <c>processing</c>, <c>completed</c>, or <c>deleted</c>.</param>
    /// <param name="sourceSid">Read only the recordings that have this <c>source_sid</c>.</param>
    /// <param name="dateCreatedAfter">Read only recordings that started on or after this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> datetime with time zone.</param>
    /// <param name="dateCreatedBefore">Read only Recordings that started before this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> datetime with time zone.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomRecordingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListRoomRecordingResponse> ListRoomRecording(string roomSid,
        RoomRecordingEnumStatus? status,
        string? sourceSid,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Recordings"),
            [new TemplateParam("RoomSid", roomSid)],
            [new Param("Status", status),
                new Param("SourceSid", sourceSid),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomRecordingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
