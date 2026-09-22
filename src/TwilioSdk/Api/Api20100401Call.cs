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

public sealed class Api20100401Call
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Call(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new outgoing call to phones, SIP-enabled endpoints or Twilio Client connections
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="to"></param>
    /// <param name="from"></param>
    /// <param name="method"></param>
    /// <param name="fallbackUrl"></param>
    /// <param name="fallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackEvent"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="sendDigits"></param>
    /// <param name="timeout"></param>
    /// <param name="record"></param>
    /// <param name="recordingChannels"></param>
    /// <param name="recordingStatusCallback"></param>
    /// <param name="recordingStatusCallbackMethod"></param>
    /// <param name="recordingConfigurationId"></param>
    /// <param name="sipAuthUsername"></param>
    /// <param name="sipAuthPassword"></param>
    /// <param name="machineDetection"></param>
    /// <param name="machineDetectionTimeout"></param>
    /// <param name="recordingStatusCallbackEvent"></param>
    /// <param name="trim"></param>
    /// <param name="callerId"></param>
    /// <param name="machineDetectionSpeechThreshold"></param>
    /// <param name="machineDetectionSpeechEndThreshold"></param>
    /// <param name="machineDetectionSilenceTimeout"></param>
    /// <param name="asyncAmd"></param>
    /// <param name="asyncAmdStatusCallback"></param>
    /// <param name="asyncAmdStatusCallbackMethod"></param>
    /// <param name="byoc"></param>
    /// <param name="callReason"></param>
    /// <param name="callToken"></param>
    /// <param name="recordingTrack"></param>
    /// <param name="timeLimit"></param>
    /// <param name="clientNotificationUrl"></param>
    /// <param name="url"></param>
    /// <param name="twiml"></param>
    /// <param name="applicationSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCall"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new outgoing call to phones, SIP-enabled endpoints or Twilio Client connections
    /// </remarks>
    public Task<ApiV2010AccountCall> CreateCall(string accountSid,
        string to,
        string from,
        Method? method,
        string? fallbackUrl,
        FallbackMethod? fallbackMethod,
        string? statusCallback,
        IReadOnlyList<string>? statusCallbackEvent,
        StatusCallbackMethod8? statusCallbackMethod,
        string? sendDigits,
        int? timeout,
        bool? record,
        string? recordingChannels,
        string? recordingStatusCallback,
        RecordingStatusCallbackMethod? recordingStatusCallbackMethod,
        string? recordingConfigurationId,
        string? sipAuthUsername,
        string? sipAuthPassword,
        string? machineDetection,
        int? machineDetectionTimeout,
        IReadOnlyList<string>? recordingStatusCallbackEvent,
        string? trim,
        string? callerId,
        int? machineDetectionSpeechThreshold,
        int? machineDetectionSpeechEndThreshold,
        int? machineDetectionSilenceTimeout,
        string? asyncAmd,
        string? asyncAmdStatusCallback,
        AsyncAmdStatusCallbackMethod? asyncAmdStatusCallbackMethod,
        string? byoc,
        string? callReason,
        string? callToken,
        string? recordingTrack,
        int? timeLimit,
        string? clientNotificationUrl,
        string? url,
        string? twiml,
        string? applicationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("To", to),
                    new Param("From", from),
                    new Param("Method", method),
                    new Param("FallbackUrl", fallbackUrl),
                    new Param("FallbackMethod", fallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackEvent", statusCallbackEvent),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("SendDigits", sendDigits),
                    new Param("Timeout", timeout),
                    new Param("Record", record),
                    new Param("RecordingChannels", recordingChannels),
                    new Param("RecordingStatusCallback", recordingStatusCallback),
                    new Param("RecordingStatusCallbackMethod", recordingStatusCallbackMethod),
                    new Param("RecordingConfigurationId", recordingConfigurationId),
                    new Param("SipAuthUsername", sipAuthUsername),
                    new Param("SipAuthPassword", sipAuthPassword),
                    new Param("MachineDetection", machineDetection),
                    new Param("MachineDetectionTimeout", machineDetectionTimeout),
                    new Param("RecordingStatusCallbackEvent", recordingStatusCallbackEvent),
                    new Param("Trim", trim),
                    new Param("CallerId", callerId),
                    new Param("MachineDetectionSpeechThreshold", machineDetectionSpeechThreshold),
                    new Param("MachineDetectionSpeechEndThreshold", machineDetectionSpeechEndThreshold),
                    new Param("MachineDetectionSilenceTimeout", machineDetectionSilenceTimeout),
                    new Param("AsyncAmd", asyncAmd),
                    new Param("AsyncAmdStatusCallback", asyncAmdStatusCallback),
                    new Param("AsyncAmdStatusCallbackMethod", asyncAmdStatusCallbackMethod),
                    new Param("Byoc", byoc),
                    new Param("CallReason", callReason),
                    new Param("CallToken", callToken),
                    new Param("RecordingTrack", recordingTrack),
                    new Param("TimeLimit", timeLimit),
                    new Param("ClientNotificationUrl", clientNotificationUrl),
                    new Param("Url", url),
                    new Param("Twiml", twiml),
                    new Param("ApplicationSid", applicationSid)]),
            JsonResponse.Create<ApiV2010AccountCall>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a Call record from your account. Once the record is deleted, it will no longer appear in the API and Account Portal logs.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call resource(s) to delete.</param>
    /// <param name="sid">The Twilio-provided Call SID that uniquely identifies the Call resource to delete</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a Call record from your account. Once the record is deleted, it will no longer appear in the API and Account Portal logs.
    /// </remarks>
    public Task DeleteCall(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
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
    /// Fetch the call specified by the provided Call SID
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call resource(s) to fetch.</param>
    /// <param name="sid">The SID of the Call resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCall"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the call specified by the provided Call SID
    /// </remarks>
    public Task<ApiV2010AccountCall> FetchCall(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountCall>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieves a collection of calls made to and from your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call resource(s) to read.</param>
    /// <param name="to">Only show calls made to this phone number, SIP address, Client identifier or SIM SID.</param>
    /// <param name="from">Only include calls from this phone number, SIP address, Client identifier or SIM SID.</param>
    /// <param name="parentCallSid">Only include calls spawned by calls with this SID.</param>
    /// <param name="status">The status of the calls to include. Can be: <c>queued</c>, <c>ringing</c>, <c>in-progress</c>, <c>canceled</c>, <c>completed</c>, <c>failed</c>, <c>busy</c>, or <c>no-answer</c>.</param>
    /// <param name="startTime">Only include calls that started on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that started on this date.</param>
    /// <param name="startTimeQuery">Only include calls that started before this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that started before this date.</param>
    /// <param name="startTimeQueryQuery">Only include calls that started on or after this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that started on or after this date.</param>
    /// <param name="endTime">Only include calls that ended on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that ended on this date.</param>
    /// <param name="endTimeQuery">Only include calls that ended before this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that ended before this date.</param>
    /// <param name="endTimeQueryQuery">Only include calls that ended on or after this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only calls that ended on or after this date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCallResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a collection of calls made to and from your account
    /// </remarks>
    public Task<ListCallResponse> ListCall(string accountSid,
        string? to,
        string? from,
        string? parentCallSid,
        CallEnumStatus? status,
        DateTimeOffset? startTime,
        DateTimeOffset? startTimeQuery,
        DateTimeOffset? startTimeQueryQuery,
        DateTimeOffset? endTime,
        DateTimeOffset? endTimeQuery,
        DateTimeOffset? endTimeQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("To", to),
                new Param("From", from),
                new Param("ParentCallSid", parentCallSid),
                new Param("Status", status),
                new Param("StartTime", startTime?.ToIso8601()),
                new Param("StartTime<", startTimeQuery?.ToIso8601()),
                new Param("StartTime>", startTimeQueryQuery?.ToIso8601()),
                new Param("EndTime", endTime?.ToIso8601()),
                new Param("EndTime<", endTimeQuery?.ToIso8601()),
                new Param("EndTime>", endTimeQueryQuery?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCallResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Initiates a call redirect or terminates a call
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call resource(s) to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Call resource to update</param>
    /// <param name="url"></param>
    /// <param name="method"></param>
    /// <param name="status"></param>
    /// <param name="fallbackUrl"></param>
    /// <param name="fallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="twiml"></param>
    /// <param name="timeLimit"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCall"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Initiates a call redirect or terminates a call
    /// </remarks>
    public Task<ApiV2010AccountCall> UpdateCall(string accountSid,
        string sid,
        string? url,
        Method1? method,
        CallEnumUpdateStatus? status,
        string? fallbackUrl,
        FallbackMethod? fallbackMethod,
        string? statusCallback,
        StatusCallbackMethod9? statusCallbackMethod,
        string? twiml,
        int? timeLimit,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Url", url),
                    new Param("Method", method),
                    new Param("Status", status),
                    new Param("FallbackUrl", fallbackUrl),
                    new Param("FallbackMethod", fallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("Twiml", twiml),
                    new Param("TimeLimit", timeLimit)]),
            JsonResponse.Create<ApiV2010AccountCall>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
