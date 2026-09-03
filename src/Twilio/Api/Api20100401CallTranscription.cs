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

public sealed class Api20100401CallTranscription
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401CallTranscription(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a Transcription
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created this Transcription resource.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Transcription resource is associated with.</param>
    /// <param name="name"></param>
    /// <param name="track"></param>
    /// <param name="statusCallbackUrl"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="inboundTrackLabel"></param>
    /// <param name="outboundTrackLabel"></param>
    /// <param name="partialResults"></param>
    /// <param name="languageCode"></param>
    /// <param name="transcriptionEngine"></param>
    /// <param name="profanityFilter"></param>
    /// <param name="speechModel"></param>
    /// <param name="hints"></param>
    /// <param name="enableAutomaticPunctuation"></param>
    /// <param name="intelligenceService"></param>
    /// <param name="conversationConfiguration"></param>
    /// <param name="conversationId"></param>
    /// <param name="transcriptionConfigurationId"></param>
    /// <param name="enableProviderData"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallRealtimeTranscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a Transcription
    /// </remarks>
    public Task<ApiV2010AccountCallRealtimeTranscription> CreateRealtimeTranscription(string accountSid,
        string callSid,
        string? name,
        RealtimeTranscriptionEnumTrack? track,
        string? statusCallbackUrl,
        StatusCallbackMethod17? statusCallbackMethod,
        string? inboundTrackLabel,
        string? outboundTrackLabel,
        bool? partialResults,
        string? languageCode,
        string? transcriptionEngine,
        bool? profanityFilter,
        string? speechModel,
        string? hints,
        bool? enableAutomaticPunctuation,
        string? intelligenceService,
        string? conversationConfiguration,
        string? conversationId,
        string? transcriptionConfigurationId,
        bool? enableProviderData,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Transcriptions.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Name", name),
                    new Param("Track", track),
                    new Param("StatusCallbackUrl", statusCallbackUrl),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("InboundTrackLabel", inboundTrackLabel),
                    new Param("OutboundTrackLabel", outboundTrackLabel),
                    new Param("PartialResults", partialResults),
                    new Param("LanguageCode", languageCode),
                    new Param("TranscriptionEngine", transcriptionEngine),
                    new Param("ProfanityFilter", profanityFilter),
                    new Param("SpeechModel", speechModel),
                    new Param("Hints", hints),
                    new Param("EnableAutomaticPunctuation", enableAutomaticPunctuation),
                    new Param("IntelligenceService", intelligenceService),
                    new Param("ConversationConfiguration", conversationConfiguration),
                    new Param("ConversationId", conversationId),
                    new Param("TranscriptionConfigurationId", transcriptionConfigurationId),
                    new Param("EnableProviderData", enableProviderData)]),
            JsonResponse.Create<ApiV2010AccountCallRealtimeTranscription>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Stop a Transcription using either the SID of the Transcription resource or the <c>name</c> used when creating the resource
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created this Transcription resource.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Transcription resource is associated with.</param>
    /// <param name="sid">The SID of the Transcription resource, or the <c>name</c> used when creating the resource</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallRealtimeTranscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Stop a Transcription using either the SID of the Transcription resource or the <c>name</c> used when creating the resource
    /// </remarks>
    public Task<ApiV2010AccountCallRealtimeTranscription> UpdateRealtimeTranscription(string accountSid,
        string callSid,
        string sid,
        RealtimeTranscriptionEnumUpdateStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Transcriptions/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<ApiV2010AccountCallRealtimeTranscription>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
