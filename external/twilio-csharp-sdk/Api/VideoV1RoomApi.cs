using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Extensions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class VideoV1RoomApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1RoomApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Video rooms with one or more participants
    /// </summary>
    /// <param name="enableTurn"></param>
    /// <param name="type"></param>
    /// <param name="uniqueName"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="maxParticipants"></param>
    /// <param name="recordParticipantsOnConnect"></param>
    /// <param name="transcribeParticipantsOnConnect"></param>
    /// <param name="videoCodecs"></param>
    /// <param name="mediaRegion"></param>
    /// <param name="recordingRules"></param>
    /// <param name="transcriptionsConfiguration"></param>
    /// <param name="audioOnly"></param>
    /// <param name="maxParticipantDuration"></param>
    /// <param name="emptyRoomTimeout"></param>
    /// <param name="unusedRoomTimeout"></param>
    /// <param name="largeRoom"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Room"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1Room> CreateRoom(bool? enableTurn,
        RoomEnumRoomType? type,
        string? uniqueName,
        string? statusCallback,
        AmdStatusCallbackMethod? statusCallbackMethod,
        int? maxParticipants,
        bool? recordParticipantsOnConnect,
        bool? transcribeParticipantsOnConnect,
        IReadOnlyList<RoomEnumVideoCodec>? videoCodecs,
        string? mediaRegion,
        object? recordingRules,
        object? transcriptionsConfiguration,
        bool? audioOnly,
        int? maxParticipantDuration,
        int? emptyRoomTimeout,
        int? unusedRoomTimeout,
        bool? largeRoom,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("EnableTurn", enableTurn),
                    new Param("Type", type),
                    new Param("UniqueName", uniqueName),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("MaxParticipants", maxParticipants),
                    new Param("RecordParticipantsOnConnect", recordParticipantsOnConnect),
                    new Param("TranscribeParticipantsOnConnect", transcribeParticipantsOnConnect),
                    new Param("VideoCodecs", videoCodecs),
                    new Param("MediaRegion", mediaRegion),
                    new Param("RecordingRules", recordingRules),
                    new Param("TranscriptionsConfiguration", transcriptionsConfiguration),
                    new Param("AudioOnly", audioOnly),
                    new Param("MaxParticipantDuration", maxParticipantDuration),
                    new Param("EmptyRoomTimeout", emptyRoomTimeout),
                    new Param("UnusedRoomTimeout", unusedRoomTimeout),
                    new Param("LargeRoom", largeRoom)]),
            JsonResponse.Create<VideoV1Room>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Video rooms with one or more participants
    /// </summary>
    /// <param name="sid">The SID of the Room resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Room"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1Room> FetchRoom(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1Room>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Video rooms with one or more participants
    /// </summary>
    /// <param name="status">Read only the rooms with this status. Can be: <c>in-progress</c> (default) or <c>completed</c></param>
    /// <param name="uniqueName">Read only rooms with the this <c>unique_name</c>.</param>
    /// <param name="dateCreatedAfter">Read only rooms that started on or after this date, given as <c>YYYY-MM-DD</c>.</param>
    /// <param name="dateCreatedBefore">Read only rooms that started before this date, given as <c>YYYY-MM-DD</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListRoomResponse> ListRoom(RecordingTranscriptionEnumStatus? status,
        string? uniqueName,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms"),
            [],
            [new Param("Status", status),
                new Param("UniqueName", uniqueName),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Video rooms with one or more participants
    /// </summary>
    /// <param name="sid">The SID of the Room resource to update.</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1Room"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1Room> UpdateRoom(string sid,
        RecordingTranscriptionEnumStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<VideoV1Room>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
