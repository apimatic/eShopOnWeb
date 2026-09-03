using System;
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

public sealed class VideoV1RecordingRules
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1RecordingRules(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Returns a list of Recording Rules for the Room.
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the recording rules to fetch apply.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomRecordingRule"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a list of Recording Rules for the Room.
    /// </remarks>
    public Task<VideoV1RoomRoomRecordingRule> FetchRoomRecordingRule(string roomSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/RecordingRules"),
            [new TemplateParam("RoomSid", roomSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomRecordingRule>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the Recording Rules for the Room
    /// </summary>
    /// <param name="roomSid">The SID of the Room resource where the recording rules to update apply.</param>
    /// <param name="rules"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomRecordingRule"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the Recording Rules for the Room
    /// </remarks>
    public Task<VideoV1RoomRoomRecordingRule> UpdateRoomRecordingRule(string roomSid,
        object? rules,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/RecordingRules"),
            [new TemplateParam("RoomSid", roomSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Rules", rules)]),
            JsonResponse.Create<VideoV1RoomRoomRecordingRule>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
