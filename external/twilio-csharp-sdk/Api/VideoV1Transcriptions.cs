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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class VideoV1Transcriptions
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1Transcriptions(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// transcriptions in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room new transcriptions resource to be created.</param>
    /// <param name="configuration"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomTranscriptions"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomTranscriptions> CreateRoomTranscriptions(string roomSid,
        object? configuration,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Transcriptions"),
            [new TemplateParam("RoomSid", roomSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Configuration", configuration)]),
            JsonResponse.Create<VideoV1RoomRoomTranscriptions>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// transcriptions in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the transcriptions resource to fetch.</param>
    /// <param name="ttid">The Twilio type id of the transcriptions resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomTranscriptions"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomTranscriptions> FetchRoomTranscriptions(string roomSid,
        string ttid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Transcriptions/{Ttid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Ttid", ttid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RoomRoomTranscriptions>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// transcriptions in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the transcriptions resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRoomTranscriptionsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListRoomTranscriptionsResponse> ListRoomTranscriptions(string roomSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Transcriptions"),
            [new TemplateParam("RoomSid", roomSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRoomTranscriptionsResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// transcriptions in video rooms
    /// </summary>
    /// <param name="roomSid">The SID of the room with the transcriptions resource to update.</param>
    /// <param name="ttid">The Twilio type id of the transcriptions resource to update.</param>
    /// <param name="status"></param>
    /// <param name="configuration"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RoomRoomTranscriptions"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RoomRoomTranscriptions> UpdateRoomTranscriptions(string roomSid,
        string ttid,
        RoomTranscriptionsEnumStatus? status,
        object? configuration,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/Rooms/{RoomSid}/Transcriptions/{Ttid}"),
            [new TemplateParam("RoomSid", roomSid), new TemplateParam("Ttid", ttid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status), new Param("Configuration", configuration)]),
            JsonResponse.Create<VideoV1RoomRoomTranscriptions>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
