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

public sealed class VideoV1SubscribeRules
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1SubscribeRules(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Returns a list of Subscribe Rules for the Participant.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the subscribe rules to fetch apply.</param>
    /// <param name="participantSid">The SID of the Participant resource with the subscribe rules to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipantRoomParticipantSubscribeRule"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a list of Subscribe Rules for the Participant.
    /// </remarks>
    public Task<VideoV1RoomRoomParticipantRoomParticipantSubscribeRule> FetchRoomParticipantSubscribeRule(string roomSid,
        string participantSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/SubscribeRules"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("ParticipantSid", participantSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomParticipantRoomParticipantSubscribeRule>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the Subscribe Rules for the Participant
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the subscribe rules to update apply.</param>
    /// <param name="participantSid">The SID of the Participant resource to update the Subscribe Rules.</param>
    /// <param name="rules"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomParticipantRoomParticipantSubscribeRule"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the Subscribe Rules for the Participant
    /// </remarks>
    public Task<VideoV1RoomRoomParticipantRoomParticipantSubscribeRule> UpdateRoomParticipantSubscribeRule(string roomSid,
        string participantSid,
        object? rules,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Participants/{ParticipantSid}/SubscribeRules"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("ParticipantSid", participantSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Rules", rules)]),
            JsonResponse.Create<VideoV1RoomRoomParticipantRoomParticipantSubscribeRule>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
