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

public sealed class InsightsV1Room
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal InsightsV1Room(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get Video Log Analyzer data for a Room.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsV1VideoRoomSummary"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get Video Log Analyzer data for a Room.
    /// </remarks>
    public Task<InsightsV1VideoRoomSummary> FetchVideoRoomSummary(string roomSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Video/Rooms/{RoomSid}"),
            [new TemplateParam("RoomSid", roomSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InsightsV1VideoRoomSummary>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get a list of Programmable Video Rooms.
    /// </summary>
    /// <param name="roomType">Type of room. Can be <c>go</c>, <c>peer_to_peer</c>, <c>group</c>, or <c>group_small</c>.</param>
    /// <param name="codec">Codecs used by participants in the room. Can be <c>VP8</c>, <c>H264</c>, or <c>VP9</c>.</param>
    /// <param name="roomName">Room friendly name.</param>
    /// <param name="createdAfter">Only read rooms that started on or after this ISO 8601 timestamp.</param>
    /// <param name="createdBefore">Only read rooms that started before this ISO 8601 timestamp.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListVideoRoomSummaryResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a list of Programmable Video Rooms.
    /// </remarks>
    public Task<ListVideoRoomSummaryResponse> ListVideoRoomSummary(IReadOnlyList<VideoRoomSummaryEnumRoomType>? roomType,
        IReadOnlyList<VideoRoomSummaryEnumCodec>? codec,
        string? roomName,
        DateTimeOffset? createdAfter,
        DateTimeOffset? createdBefore,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Video/Rooms"),
            [],
            [new Param("RoomType", roomType),
                new Param("Codec", codec),
                new Param("RoomName", roomName),
                new Param("CreatedAfter", createdAfter?.ToIso8601()),
                new Param("CreatedBefore", createdBefore?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListVideoRoomSummaryResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
