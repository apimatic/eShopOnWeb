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

public sealed class VideoV1SubscribedTrack
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1SubscribedTrack(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Returns a single Track resource represented by <c>track_sid</c>.  Note: This is one resource with the Video API that requires a SID, be Track Name on the subscriber side is not guaranteed to be unique.
    /// </summary>
    /// <param name="roomSid">The SID of the Room where the Track resource to fetch is subscribed.</param>
    /// <param name="participantSid">The SID of the participant that subscribes to the Track resource to fetch.</param>
    /// <param name="sid">The SID of the RoomParticipantSubscribedTrack resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipantRoomParticipantSubscribedTrack"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a single Track resource represented by <c>track_sid</c>.  Note: This is one resource with the Video API that requires a SID, be Track Name on the subscriber side is not guaranteed to be unique.
    /// </remarks>
    public Task<VideoV1RoomRoomParticipantRoomParticipantSubscribedTrack> FetchRoomParticipantSubscribedTrack(string roomSid,
        string participantSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/SubscribedTracks/{Sid}"),
            [new TemplateParam("RoomSid", roomSid),
                new TemplateParam("ParticipantSid", participantSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomParticipantRoomParticipantSubscribedTrack>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Returns a list of tracks that are subscribed for the participant.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource with the Track resources to read.</param>
    /// <param name="participantSid">The SID of the participant that subscribes to the Track resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomParticipantSubscribedTrackResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a list of tracks that are subscribed for the participant.
    /// </remarks>
    public Task<ListRoomParticipantSubscribedTrackResponse> ListRoomParticipantSubscribedTrack(string roomSid,
        string participantSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/SubscribedTracks"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("ParticipantSid", participantSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomParticipantSubscribedTrackResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
