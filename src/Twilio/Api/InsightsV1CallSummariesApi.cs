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

public sealed class InsightsV1CallSummariesApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal InsightsV1CallSummariesApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get a list of Call Summaries.
    /// </summary>
    /// <param name="from">A calling party. Could be an E.164 number, a SIP URI, or a Twilio Client registered name.</param>
    /// <param name="to">A called party. Could be an E.164 number, a SIP URI, or a Twilio Client registered name.</param>
    /// <param name="fromCarrier">An origination carrier.</param>
    /// <param name="toCarrier">A destination carrier.</param>
    /// <param name="fromCountryCode">A source country code based on phone number in From.</param>
    /// <param name="toCountryCode">A destination country code. Based on phone number in To.</param>
    /// <param name="verifiedCaller">A boolean flag indicating whether or not the caller was verified using SHAKEN/STIR.One of 'true' or 'false'.</param>
    /// <param name="hasTag">A boolean flag indicating the presence of one or more <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-tags">Voice Insights Call Tags</see>.</param>
    /// <param name="startTime">A Start time of the calls. xm (x minutes), xh (x hours), xd (x days), 1w, 30m, 3d, 4w or datetime-ISO. Defaults to 4h.</param>
    /// <param name="endTime">An End Time of the calls. xm (x minutes), xh (x hours), xd (x days), 1w, 30m, 3d, 4w or datetime-ISO. Defaults to 0m.</param>
    /// <param name="callType">A Call Type of the calls. One of <c>carrier</c>, <c>sip</c>, <c>trunking</c> or <c>client</c>.</param>
    /// <param name="callState">A Call State of the calls. One of <c>ringing</c>, <c>completed</c>, <c>busy</c>, <c>fail</c>, <c>noanswer</c>, <c>canceled</c>, <c>answered</c>, <c>undialed</c>.</param>
    /// <param name="direction">A Direction of the calls. One of <c>outbound_api</c>, <c>outbound_dial</c>, <c>inbound</c>, <c>trunking_originating</c>, <c>trunking_terminating</c>.</param>
    /// <param name="processingState">A Processing State of the Call Summaries. One of <c>completed</c>, <c>partial</c> or <c>all</c>.</param>
    /// <param name="sortBy">A Sort By criterion for the returned list of Call Summaries. One of <c>start_time</c> or <c>end_time</c>.</param>
    /// <param name="subaccount">A unique SID identifier of a Subaccount.</param>
    /// <param name="abnormalSession">A boolean flag indicating an abnormal session where the last SIP response was not 200 OK.</param>
    /// <param name="answeredBy">An Answered By value for the calls based on <c>Answering Machine Detection (AMD)</c>. One of <c>unknown</c>, <c>machine_start</c>, <c>machine_end_beep</c>, <c>machine_end_silence</c>, <c>machine_end_other</c>, <c>human</c> or <c>fax</c>.</param>
    /// <param name="answeredByAnnotation">Either machine or human.</param>
    /// <param name="connectivityIssueAnnotation">A Connectivity Issue with the calls. One of <c>no_connectivity_issue</c>, <c>invalid_number</c>, <c>caller_id</c>, <c>dropped_call</c>, or <c>number_reachability</c>.</param>
    /// <param name="qualityIssueAnnotation">A subjective Quality Issue with the calls. One of <c>no_quality_issue</c>, <c>low_volume</c>, <c>choppy_robotic</c>, <c>echo</c>, <c>dtmf</c>, <c>latency</c>, <c>owa</c>, <c>static_noise</c>.</param>
    /// <param name="spamAnnotation">A boolean flag indicating spam calls.</param>
    /// <param name="callScoreAnnotation">A Call Score of the calls. Use a range of 1-5 to indicate the call experience score, with the following mapping as a reference for the rated call [5: Excellent, 4: Good, 3 : Fair, 2 : Poor, 1: Bad].</param>
    /// <param name="brandedEnabled">A boolean flag indicating whether or not the calls were branded using Twilio Branded Calls. One of 'true' or 'false'</param>
    /// <param name="voiceIntegrityEnabled">A boolean flag indicating whether or not the phone number had voice integrity enabled.One of 'true' or 'false'</param>
    /// <param name="brandedBundleSid">A unique SID identifier of the Branded Call.</param>
    /// <param name="brandedLogo">Indicates whether the branded logo was displayed during the in_brand branded call. Possible values are true (logo was present) or false (logo was not present).</param>
    /// <param name="brandedType">Indicates whether the Branded Call is in_band vs out_of_band.</param>
    /// <param name="brandedUseCase">Specifies the user-defined purpose for the call, as provided during the setup of in_band branded calling.</param>
    /// <param name="brandedCallReason">Specifies the user-defined reason for the call, which will be displayed to the end user on their mobile device during an in_band branded call.</param>
    /// <param name="voiceIntegrityBundleSid">A unique SID identifier of the Voice Integrity Profile.</param>
    /// <param name="voiceIntegrityUseCase">A Voice Integrity Use Case . Is of type enum. One of 'abandoned_cart', 'appointment_reminders', 'appointment_scheduling', 'asset_management', 'automated_support', 'call_tracking', 'click_to_call', 'contact_tracing', 'contactless_delivery', 'customer_support', 'dating/social', 'delivery_notifications', 'distance_learning', 'emergency_notifications', 'employee_notifications', 'exam_proctoring', 'field_notifications', 'first_responder', 'fraud_alerts', 'group_messaging', 'identify_&amp;_verification', 'intelligent_routing', 'lead_alerts', 'lead_distribution', 'lead_generation', 'lead_management', 'lead_nurturing', 'marketing_events', 'mass_alerts', 'meetings/collaboration', 'order_notifications', 'outbound_dialer', 'pharmacy', 'phone_system', 'purchase_confirmation', 'remote_appointments', 'rewards_program', 'self-service', 'service_alerts', 'shift_management', 'survey/research', 'telehealth', 'telemarketing', 'therapy_(individual+group)'.</param>
    /// <param name="businessProfileIdentity">A Business Identity of the calls. Is of type enum. One of 'direct_customer', 'isv_reseller_or_partner'.</param>
    /// <param name="businessProfileIndustry">A Business Industry of the calls. Is of type enum. One of 'automotive', 'agriculture', 'banking', 'consumer', 'construction', 'education', 'engineering', 'energy', 'oil_and_gas', 'fast_moving_consumer_goods', 'financial', 'fintech', 'food_and_beverage', 'government', 'healthcare', 'hospitality', 'insurance', 'legal', 'manufacturing', 'media', 'online', 'professional_services', 'raw_materials', 'real_estate', 'religion', 'retail', 'jewelry', 'technology', 'telecommunications', 'transportation', 'travel', 'electronics', 'not_for_profit'</param>
    /// <param name="businessProfileBundleSid">A unique SID identifier of the Business Profile.</param>
    /// <param name="businessProfileType">A Business Profile Type of the calls. Is of type enum. One of 'primary', 'secondary'.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCallSummariesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a list of Call Summaries.
    /// </remarks>
    public Task<ListCallSummariesResponse> ListCallSummaries(string? from,
        string? to,
        string? fromCarrier,
        string? toCarrier,
        string? fromCountryCode,
        string? toCountryCode,
        bool? verifiedCaller,
        bool? hasTag,
        string? startTime,
        string? endTime,
        string? callType,
        string? callState,
        string? direction,
        CallSummariesEnumProcessingStateRequest? processingState,
        CallSummariesEnumSortBy? sortBy,
        string? subaccount,
        bool? abnormalSession,
        CallSummariesEnumAnsweredBy? answeredBy,
        string? answeredByAnnotation,
        string? connectivityIssueAnnotation,
        string? qualityIssueAnnotation,
        bool? spamAnnotation,
        string? callScoreAnnotation,
        bool? brandedEnabled,
        bool? voiceIntegrityEnabled,
        string? brandedBundleSid,
        bool? brandedLogo,
        string? brandedType,
        string? brandedUseCase,
        string? brandedCallReason,
        string? voiceIntegrityBundleSid,
        string? voiceIntegrityUseCase,
        string? businessProfileIdentity,
        string? businessProfileIndustry,
        string? businessProfileBundleSid,
        string? businessProfileType,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Voice/Summaries"),
            [],
            [new Param("From", from),
                new Param("To", to),
                new Param("FromCarrier", fromCarrier),
                new Param("ToCarrier", toCarrier),
                new Param("FromCountryCode", fromCountryCode),
                new Param("ToCountryCode", toCountryCode),
                new Param("VerifiedCaller", verifiedCaller),
                new Param("HasTag", hasTag),
                new Param("StartTime", startTime),
                new Param("EndTime", endTime),
                new Param("CallType", callType),
                new Param("CallState", callState),
                new Param("Direction", direction),
                new Param("ProcessingState", processingState),
                new Param("SortBy", sortBy),
                new Param("Subaccount", subaccount),
                new Param("AbnormalSession", abnormalSession),
                new Param("AnsweredBy", answeredBy),
                new Param("AnsweredByAnnotation", answeredByAnnotation),
                new Param("ConnectivityIssueAnnotation", connectivityIssueAnnotation),
                new Param("QualityIssueAnnotation", qualityIssueAnnotation),
                new Param("SpamAnnotation", spamAnnotation),
                new Param("CallScoreAnnotation", callScoreAnnotation),
                new Param("BrandedEnabled", brandedEnabled),
                new Param("VoiceIntegrityEnabled", voiceIntegrityEnabled),
                new Param("BrandedBundleSid", brandedBundleSid),
                new Param("BrandedLogo", brandedLogo),
                new Param("BrandedType", brandedType),
                new Param("BrandedUseCase", brandedUseCase),
                new Param("BrandedCallReason", brandedCallReason),
                new Param("VoiceIntegrityBundleSid", voiceIntegrityBundleSid),
                new Param("VoiceIntegrityUseCase", voiceIntegrityUseCase),
                new Param("BusinessProfileIdentity", businessProfileIdentity),
                new Param("BusinessProfileIndustry", businessProfileIndustry),
                new Param("BusinessProfileBundleSid", businessProfileBundleSid),
                new Param("BusinessProfileType", businessProfileType),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCallSummariesResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
