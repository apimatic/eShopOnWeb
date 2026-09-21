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

public sealed class Api20100401Participant
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Participant(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Conference participants
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="conferenceSid">The SID of the participant's conference.</param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="statusCallbackEvent"></param>
    /// <param name="label"></param>
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
    /// <param name="conferenceRecord"></param>
    /// <param name="conferenceTrim"></param>
    /// <param name="conferenceStatusCallback"></param>
    /// <param name="conferenceStatusCallbackMethod"></param>
    /// <param name="conferenceStatusCallbackEvent"></param>
    /// <param name="recordingChannels"></param>
    /// <param name="recordingStatusCallback"></param>
    /// <param name="recordingStatusCallbackMethod"></param>
    /// <param name="sipAuthUsername"></param>
    /// <param name="sipAuthPassword"></param>
    /// <param name="region"></param>
    /// <param name="conferenceRecordingStatusCallback"></param>
    /// <param name="conferenceRecordingStatusCallbackMethod"></param>
    /// <param name="recordingStatusCallbackEvent"></param>
    /// <param name="conferenceRecordingStatusCallbackEvent"></param>
    /// <param name="coaching"></param>
    /// <param name="callSidToCoach"></param>
    /// <param name="jitterBufferSize"></param>
    /// <param name="byoc"></param>
    /// <param name="callerId"></param>
    /// <param name="callReason"></param>
    /// <param name="recordingTrack"></param>
    /// <param name="recordingConfigurationId"></param>
    /// <param name="timeLimit"></param>
    /// <param name="machineDetection"></param>
    /// <param name="machineDetectionTimeout"></param>
    /// <param name="machineDetectionSpeechThreshold"></param>
    /// <param name="machineDetectionSpeechEndThreshold"></param>
    /// <param name="machineDetectionSilenceTimeout"></param>
    /// <param name="amdStatusCallback"></param>
    /// <param name="amdStatusCallbackMethod"></param>
    /// <param name="trim"></param>
    /// <param name="callToken"></param>
    /// <param name="clientNotificationUrl"></param>
    /// <param name="callerDisplayName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConferenceParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountConferenceParticipant> CreateParticipant(string accountSid,
        string conferenceSid,
        string from,
        string to,
        string? statusCallback,
        StatusCallbackMethod16? statusCallbackMethod,
        IReadOnlyList<string>? statusCallbackEvent,
        string? label,
        int? timeout,
        bool? record,
        bool? muted,
        string? beep,
        bool? startConferenceOnEnter,
        bool? endConferenceOnExit,
        string? waitUrl,
        WaitMethod? waitMethod,
        bool? earlyMedia,
        int? maxParticipants,
        string? conferenceRecord,
        string? conferenceTrim,
        string? conferenceStatusCallback,
        ConferenceStatusCallbackMethod? conferenceStatusCallbackMethod,
        IReadOnlyList<string>? conferenceStatusCallbackEvent,
        string? recordingChannels,
        string? recordingStatusCallback,
        RecordingStatusCallbackMethod2? recordingStatusCallbackMethod,
        string? sipAuthUsername,
        string? sipAuthPassword,
        string? region,
        string? conferenceRecordingStatusCallback,
        ConferenceRecordingStatusCallbackMethod? conferenceRecordingStatusCallbackMethod,
        IReadOnlyList<string>? recordingStatusCallbackEvent,
        IReadOnlyList<string>? conferenceRecordingStatusCallbackEvent,
        bool? coaching,
        string? callSidToCoach,
        string? jitterBufferSize,
        string? byoc,
        string? callerId,
        string? callReason,
        string? recordingTrack,
        string? recordingConfigurationId,
        int? timeLimit,
        string? machineDetection,
        int? machineDetectionTimeout,
        int? machineDetectionSpeechThreshold,
        int? machineDetectionSpeechEndThreshold,
        int? machineDetectionSilenceTimeout,
        string? amdStatusCallback,
        AmdStatusCallbackMethod? amdStatusCallbackMethod,
        string? trim,
        string? callToken,
        string? clientNotificationUrl,
        string? callerDisplayName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Participants.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("ConferenceSid", conferenceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("From", from),
                    new Param("To", to),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("StatusCallbackEvent", statusCallbackEvent),
                    new Param("Label", label),
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
                    new Param("ConferenceRecord", conferenceRecord),
                    new Param("ConferenceTrim", conferenceTrim),
                    new Param("ConferenceStatusCallback", conferenceStatusCallback),
                    new Param("ConferenceStatusCallbackMethod", conferenceStatusCallbackMethod),
                    new Param("ConferenceStatusCallbackEvent", conferenceStatusCallbackEvent),
                    new Param("RecordingChannels", recordingChannels),
                    new Param("RecordingStatusCallback", recordingStatusCallback),
                    new Param("RecordingStatusCallbackMethod", recordingStatusCallbackMethod),
                    new Param("SipAuthUsername", sipAuthUsername),
                    new Param("SipAuthPassword", sipAuthPassword),
                    new Param("Region", region),
                    new Param("ConferenceRecordingStatusCallback", conferenceRecordingStatusCallback),
                    new Param("ConferenceRecordingStatusCallbackMethod", conferenceRecordingStatusCallbackMethod),
                    new Param("RecordingStatusCallbackEvent", recordingStatusCallbackEvent),
                    new Param("ConferenceRecordingStatusCallbackEvent", conferenceRecordingStatusCallbackEvent),
                    new Param("Coaching", coaching),
                    new Param("CallSidToCoach", callSidToCoach),
                    new Param("JitterBufferSize", jitterBufferSize),
                    new Param("Byoc", byoc),
                    new Param("CallerId", callerId),
                    new Param("CallReason", callReason),
                    new Param("RecordingTrack", recordingTrack),
                    new Param("RecordingConfigurationId", recordingConfigurationId),
                    new Param("TimeLimit", timeLimit),
                    new Param("MachineDetection", machineDetection),
                    new Param("MachineDetectionTimeout", machineDetectionTimeout),
                    new Param("MachineDetectionSpeechThreshold", machineDetectionSpeechThreshold),
                    new Param("MachineDetectionSpeechEndThreshold", machineDetectionSpeechEndThreshold),
                    new Param("MachineDetectionSilenceTimeout", machineDetectionSilenceTimeout),
                    new Param("AmdStatusCallback", amdStatusCallback),
                    new Param("AmdStatusCallbackMethod", amdStatusCallbackMethod),
                    new Param("Trim", trim),
                    new Param("CallToken", callToken),
                    new Param("ClientNotificationUrl", clientNotificationUrl),
                    new Param("CallerDisplayName", callerDisplayName)]),
            JsonResponse.Create<ApiV2010AccountConferenceParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Kick a participant from a given conference
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Participant resources to delete.</param>
    /// <param name="conferenceSid">The SID of the conference with the participants to delete.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID or label of the participant to delete. Non URL safe characters in a label must be percent encoded, for example, a space character is represented as %20.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Kick a participant from a given conference
    /// </remarks>
    public Task DeleteParticipant(string accountSid,
        string conferenceSid,
        string callSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Participants/{CallSid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("CallSid", callSid)],
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
    /// Fetch an instance of a participant
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Participant resource to fetch.</param>
    /// <param name="conferenceSid">The SID of the conference with the participant to fetch.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID or label of the participant to fetch. Non URL safe characters in a label must be percent encoded, for example, a space character is represented as %20.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConferenceParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a participant
    /// </remarks>
    public Task<ApiV2010AccountConferenceParticipant> FetchParticipant(string accountSid,
        string conferenceSid,
        string callSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Participants/{CallSid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("CallSid", callSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountConferenceParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of participants belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Participant resources to read.</param>
    /// <param name="conferenceSid">The SID of the conference with the participants to read.</param>
    /// <param name="muted">Whether to return only participants that are muted. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="hold">Whether to return only participants that are on hold. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="coaching">Whether to return only participants who are coaching another call. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListParticipantResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of participants belonging to the account used to make the request
    /// </remarks>
    public Task<ListParticipantResponse> ListParticipant(string accountSid,
        string conferenceSid,
        bool? muted,
        bool? hold,
        bool? coaching,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Participants.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("ConferenceSid", conferenceSid)],
            [new Param("Muted", muted),
                new Param("Hold", hold),
                new Param("Coaching", coaching),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListParticipantResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the properties of the participant
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Participant resources to update.</param>
    /// <param name="conferenceSid">The SID of the conference with the participant to update.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID or label of the participant to update. Non URL safe characters in a label must be percent encoded, for example, a space character is represented as %20.</param>
    /// <param name="muted"></param>
    /// <param name="hold"></param>
    /// <param name="holdUrl"></param>
    /// <param name="holdMethod"></param>
    /// <param name="announceUrl"></param>
    /// <param name="announceMethod"></param>
    /// <param name="waitUrl"></param>
    /// <param name="waitMethod"></param>
    /// <param name="beepOnExit"></param>
    /// <param name="endConferenceOnExit"></param>
    /// <param name="coaching"></param>
    /// <param name="callSidToCoach"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConferenceParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the properties of the participant
    /// </remarks>
    public Task<ApiV2010AccountConferenceParticipant> UpdateParticipant(string accountSid,
        string conferenceSid,
        string callSid,
        bool? muted,
        bool? hold,
        string? holdUrl,
        HoldMethod? holdMethod,
        string? announceUrl,
        AnnounceMethod1? announceMethod,
        string? waitUrl,
        WaitMethod? waitMethod,
        bool? beepOnExit,
        bool? endConferenceOnExit,
        bool? coaching,
        string? callSidToCoach,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Participants/{CallSid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Muted", muted),
                    new Param("Hold", hold),
                    new Param("HoldUrl", holdUrl),
                    new Param("HoldMethod", holdMethod),
                    new Param("AnnounceUrl", announceUrl),
                    new Param("AnnounceMethod", announceMethod),
                    new Param("WaitUrl", waitUrl),
                    new Param("WaitMethod", waitMethod),
                    new Param("BeepOnExit", beepOnExit),
                    new Param("EndConferenceOnExit", endConferenceOnExit),
                    new Param("Coaching", coaching),
                    new Param("CallSidToCoach", callSidToCoach)]),
            JsonResponse.Create<ApiV2010AccountConferenceParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
