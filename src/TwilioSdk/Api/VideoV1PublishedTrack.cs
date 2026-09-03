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

public sealed class VideoV1PublishedTrack
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1PublishedTrack(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Returns a single Track resource represented by TrackName or SID.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the Track resource to fetch is published.</param>
    /// <param name="participantSid">The SID of the Participant resource with the published track to fetch.</param>
    /// <param name="sid">The SID of the RoomParticipantPublishedTrack resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipantRoomParticipantPublishedTrack"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a single Track resource represented by TrackName or SID.
    /// </remarks>
    public Task<VideoV1RoomRoomParticipantRoomParticipantPublishedTrack> FetchRoomParticipantPublishedTrack(string roomSid,
        string participantSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/PublishedTracks/{Sid}"),
            [new TemplateParam("RoomSid", roomSid),
                new TemplateParam("ParticipantSid", participantSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomParticipantRoomParticipantPublishedTrack>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Returns a list of tracks associated with a given Participant. Only <c>currently</c> Published Tracks are in the list resource.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the Track resources to read are published.</param>
    /// <param name="participantSid">The SID of the Participant resource with the published tracks to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomParticipantPublishedTrackResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a list of tracks associated with a given Participant. Only <c>currently</c> Published Tracks are in the list resource.
    /// </remarks>
    public Task<ListRoomParticipantPublishedTrackResponse> ListRoomParticipantPublishedTrack(string roomSid,
        string participantSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/PublishedTracks"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("ParticipantSid", participantSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomParticipantPublishedTrackResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
