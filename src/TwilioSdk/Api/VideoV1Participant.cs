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

public sealed class VideoV1Participant
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1Participant(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Participants in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the Participant resource to fetch.</param>
    /// <param name="sid">The SID of the RoomParticipant resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomParticipant> FetchRoomParticipant(string roomSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{Sid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Participants in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the Participant resources to read.</param>
    /// <param name="status">Read only the participants with this status. Can be: <c>connected</c> or <c>disconnected</c>. For <c>in-progress</c> Rooms the default Status is <c>connected</c>, for <c>completed</c> Rooms only <c>disconnected</c> Participants are returned.</param>
    /// <param name="identity">Read only the Participants with this <see href="https://www.twilio.com/docs/chat/rest/user-resource">User</see> <c>identity</c> value.</param>
    /// <param name="dateCreatedAfter">Read only Participants that started after this date in <see href="https://en.wikipedia.org/wiki/ISO_8601#UTC">ISO 8601</see> format.</param>
    /// <param name="dateCreatedBefore">Read only Participants that started before this date in <see href="https://en.wikipedia.org/wiki/ISO_8601#UTC">ISO 8601</see> format.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomParticipantResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListRoomParticipantResponse> ListRoomParticipant(string roomSid,
        RoomParticipantEnumStatus? status,
        string? identity,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants"),
            [new TemplateParam("RoomSid", roomSid)],
            [new Param("Status", status),
                new Param("Identity", identity),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomParticipantResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Participants in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the participant to update.</param>
    /// <param name="sid">The SID of the RoomParticipant resource to update.</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomParticipant> UpdateRoomParticipant(string roomSid,
        string sid,
        RoomParticipantEnumStatus? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{Sid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<VideoV1RoomRoomParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
