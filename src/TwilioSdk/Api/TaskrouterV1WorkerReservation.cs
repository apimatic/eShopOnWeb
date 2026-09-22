using System;
using System.Collections.Generic;
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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class TaskrouterV1WorkerReservation
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TaskrouterV1WorkerReservation(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Current and past reservations for a worker
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the WorkerReservation resource to fetch.</param>
    /// <param name="workerSid">The SID of the reserved Worker resource with the WorkerReservation resource to fetch.</param>
    /// <param name="sid">The SID of the WorkerReservation resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TaskrouterV1WorkspaceWorkerWorkerReservation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<TaskrouterV1WorkspaceWorkerWorkerReservation> FetchWorkerReservation(string workspaceSid,
        string workerSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Reservations/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid),
                new TemplateParam("WorkerSid", workerSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TaskrouterV1WorkspaceWorkerWorkerReservation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Current and past reservations for a worker
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the WorkerReservation resources to read.</param>
    /// <param name="workerSid">The SID of the reserved Worker resource with the WorkerReservation resources to read.</param>
    /// <param name="reservationStatus">Returns the list of reservations for a worker with a specified ReservationStatus. Can be: <c>pending</c>, <c>accepted</c>, <c>rejected</c>, <c>timeout</c>, <c>canceled</c>, or <c>rescinded</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListWorkerReservationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListWorkerReservationResponse> ListWorkerReservation(string workspaceSid,
        string workerSid,
        WorkerReservationEnumStatus? reservationStatus,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Reservations"),
            [new TemplateParam("WorkspaceSid", workspaceSid), new TemplateParam("WorkerSid", workerSid)],
            [new Param("ReservationStatus", reservationStatus),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListWorkerReservationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Current and past reservations for a worker
    /// </summary>
    /// <param name="workspaceSid">The SID of the Workspace with the WorkerReservation resources to update.</param>
    /// <param name="workerSid">The SID of the reserved Worker resource with the WorkerReservation resources to update.</param>
    /// <param name="sid">The SID of the WorkerReservation resource to update.</param>
    /// <param name="ifMatch">The If-Match HTTP request header</param>
    /// <param name="reservationStatus"></param>
    /// <param name="workerActivitySid"></param>
    /// <param name="instruction"></param>
    /// <param name="dequeuePostWorkActivitySid"></param>
    /// <param name="dequeueFrom"></param>
    /// <param name="dequeueRecord"></param>
    /// <param name="dequeueTimeout"></param>
    /// <param name="dequeueTo"></param>
    /// <param name="dequeueStatusCallbackUrl"></param>
    /// <param name="callFrom"></param>
    /// <param name="callRecord"></param>
    /// <param name="callTimeout"></param>
    /// <param name="callTo"></param>
    /// <param name="callUrl"></param>
    /// <param name="callStatusCallbackUrl"></param>
    /// <param name="callAccept"></param>
    /// <param name="redirectCallSid"></param>
    /// <param name="redirectAccept"></param>
    /// <param name="redirectUrl"></param>
    /// <param name="to"></param>
    /// <param name="from"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="statusCallbackEvent"></param>
    /// <param name="timeout"></param>
    /// <param name="record"></param>
    /// <param name="muted"></param>
    /// <param name="beep"></param>
    /// <param name="startConferenceOnEnter"></param>
    /// <param name="endConferenceOnExit"></param>
    /// <param name="waitUrl"></param>
    /// <param name="waitMethod"></param>
    /// <param name="earlyMedia"></param>
    /// <param name="maxParticipants"></param>
    /// <param name="conferenceStatusCallback"></param>
    /// <param name="conferenceStatusCallbackMethod"></param>
    /// <param name="conferenceStatusCallbackEvent"></param>
    /// <param name="conferenceRecord"></param>
    /// <param name="conferenceTrim"></param>
    /// <param name="recordingChannels"></param>
    /// <param name="recordingStatusCallback"></param>
    /// <param name="recordingStatusCallbackMethod"></param>
    /// <param name="conferenceRecordingStatusCallback"></param>
    /// <param name="conferenceRecordingStatusCallbackMethod"></param>
    /// <param name="region"></param>
    /// <param name="sipAuthUsername"></param>
    /// <param name="sipAuthPassword"></param>
    /// <param name="dequeueStatusCallbackEvent"></param>
    /// <param name="postWorkActivitySid"></param>
    /// <param name="endConferenceOnCustomerExit"></param>
    /// <param name="beepOnCustomerEntrance"></param>
    /// <param name="jitterBufferSize"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TaskrouterV1WorkspaceWorkerWorkerReservation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<TaskrouterV1WorkspaceWorkerWorkerReservation> UpdateWorkerReservation(string workspaceSid,
        string workerSid,
        string sid,
        string? ifMatch,
        WorkerReservationEnumStatus? reservationStatus,
        string? workerActivitySid,
        string? instruction,
        string? dequeuePostWorkActivitySid,
        string? dequeueFrom,
        string? dequeueRecord,
        int? dequeueTimeout,
        string? dequeueTo,
        string? dequeueStatusCallbackUrl,
        string? callFrom,
        string? callRecord,
        int? callTimeout,
        string? callTo,
        string? callUrl,
        string? callStatusCallbackUrl,
        bool? callAccept,
        string? redirectCallSid,
        bool? redirectAccept,
        string? redirectUrl,
        string? to,
        string? from,
        string? statusCallback,
        AmdStatusCallbackMethod? statusCallbackMethod,
        IReadOnlyList<CallEnumEvent>? statusCallbackEvent,
        int? timeout,
        bool? record,
        bool? muted,
        string? beep,
        bool? startConferenceOnEnter,
        bool? endConferenceOnExit,
        string? waitUrl,
        AmdStatusCallbackMethod? waitMethod,
        bool? earlyMedia,
        int? maxParticipants,
        string? conferenceStatusCallback,
        AmdStatusCallbackMethod? conferenceStatusCallbackMethod,
        IReadOnlyList<WorkerReservationEnumConferenceEvent>? conferenceStatusCallbackEvent,
        string? conferenceRecord,
        string? conferenceTrim,
        string? recordingChannels,
        string? recordingStatusCallback,
        AmdStatusCallbackMethod? recordingStatusCallbackMethod,
        string? conferenceRecordingStatusCallback,
        AmdStatusCallbackMethod? conferenceRecordingStatusCallbackMethod,
        string? region,
        string? sipAuthUsername,
        string? sipAuthPassword,
        IReadOnlyList<string>? dequeueStatusCallbackEvent,
        string? postWorkActivitySid,
        bool? endConferenceOnCustomerExit,
        bool? beepOnCustomerEntrance,
        string? jitterBufferSize,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default8("/v1/Workspaces/{WorkspaceSid}/Workers/{WorkerSid}/Reservations/{Sid}"),
            [new TemplateParam("WorkspaceSid", workspaceSid),
                new TemplateParam("WorkerSid", workerSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("ReservationStatus", reservationStatus),
                    new Param("WorkerActivitySid", workerActivitySid),
                    new Param("Instruction", instruction),
                    new Param("DequeuePostWorkActivitySid", dequeuePostWorkActivitySid),
                    new Param("DequeueFrom", dequeueFrom),
                    new Param("DequeueRecord", dequeueRecord),
                    new Param("DequeueTimeout", dequeueTimeout),
                    new Param("DequeueTo", dequeueTo),
                    new Param("DequeueStatusCallbackUrl", dequeueStatusCallbackUrl),
                    new Param("CallFrom", callFrom),
                    new Param("CallRecord", callRecord),
                    new Param("CallTimeout", callTimeout),
                    new Param("CallTo", callTo),
                    new Param("CallUrl", callUrl),
                    new Param("CallStatusCallbackUrl", callStatusCallbackUrl),
                    new Param("CallAccept", callAccept),
                    new Param("RedirectCallSid", redirectCallSid),
                    new Param("RedirectAccept", redirectAccept),
                    new Param("RedirectUrl", redirectUrl),
                    new Param("To", to),
                    new Param("From", from),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("StatusCallbackEvent", statusCallbackEvent),
                    new Param("Timeout", timeout),
                    new Param("Record", record),
                    new Param("Muted", muted),
                    new Param("Beep", beep),
                    new Param("StartConferenceOnEnter", startConferenceOnEnter),
                    new Param("EndConferenceOnExit", endConferenceOnExit),
                    new Param("WaitUrl", waitUrl),
                    new Param("WaitMethod", waitMethod),
                    new Param("EarlyMedia", earlyMedia),
                    new Param("MaxParticipants", maxParticipants),
                    new Param("ConferenceStatusCallback", conferenceStatusCallback),
                    new Param("ConferenceStatusCallbackMethod", conferenceStatusCallbackMethod),
                    new Param("ConferenceStatusCallbackEvent", conferenceStatusCallbackEvent),
                    new Param("ConferenceRecord", conferenceRecord),
                    new Param("ConferenceTrim", conferenceTrim),
                    new Param("RecordingChannels", recordingChannels),
                    new Param("RecordingStatusCallback", recordingStatusCallback),
                    new Param("RecordingStatusCallbackMethod", recordingStatusCallbackMethod),
                    new Param("ConferenceRecordingStatusCallback", conferenceRecordingStatusCallback),
                    new Param("ConferenceRecordingStatusCallbackMethod", conferenceRecordingStatusCallbackMethod),
                    new Param("Region", region),
                    new Param("SipAuthUsername", sipAuthUsername),
                    new Param("SipAuthPassword", sipAuthPassword),
                    new Param("DequeueStatusCallbackEvent", dequeueStatusCallbackEvent),
                    new Param("PostWorkActivitySid", postWorkActivitySid),
                    new Param("EndConferenceOnCustomerExit", endConferenceOnCustomerExit),
                    new Param("BeepOnCustomerEntrance", beepOnCustomerEntrance),
                    new Param("JitterBufferSize", jitterBufferSize)]),
            JsonResponse.Create<TaskrouterV1WorkspaceWorkerWorkerReservation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
