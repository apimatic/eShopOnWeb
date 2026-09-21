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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class Api20100401IncomingPhoneNumberLocal
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401IncomingPhoneNumberLocal(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Incoming local phone numbers on a Twilio account/project
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="phoneNumber"></param>
    /// <param name="apiVersion"></param>
    /// <param name="friendlyName"></param>
    /// <param name="smsApplicationSid"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsUrl"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="voiceApplicationSid"></param>
    /// <param name="voiceCallerIdLookup"></param>
    /// <param name="voiceFallbackMethod"></param>
    /// <param name="voiceFallbackUrl"></param>
    /// <param name="voiceMethod"></param>
    /// <param name="voiceUrl"></param>
    /// <param name="identitySid"></param>
    /// <param name="addressSid"></param>
    /// <param name="emergencyStatus"></param>
    /// <param name="emergencyAddressSid"></param>
    /// <param name="trunkSid"></param>
    /// <param name="voiceReceiveMode"></param>
    /// <param name="bundleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberLocal"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberLocal> CreateIncomingPhoneNumberLocal(string accountSid,
        string phoneNumber,
        string? apiVersion,
        string? friendlyName,
        string? smsApplicationSid,
        SmsFallbackMethod9? smsFallbackMethod,
        string? smsFallbackUrl,
        SmsMethod9? smsMethod,
        string? smsUrl,
        string? statusCallback,
        StatusCallbackMethod10? statusCallbackMethod,
        string? voiceApplicationSid,
        bool? voiceCallerIdLookup,
        VoiceFallbackMethod9? voiceFallbackMethod,
        string? voiceFallbackUrl,
        VoiceMethod9? voiceMethod,
        string? voiceUrl,
        string? identitySid,
        string? addressSid,
        IncomingPhoneNumberLocalEnumEmergencyStatus? emergencyStatus,
        string? emergencyAddressSid,
        string? trunkSid,
        IncomingPhoneNumberLocalEnumVoiceReceiveMode? voiceReceiveMode,
        string? bundleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/Local.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PhoneNumber", phoneNumber),
                    new Param("ApiVersion", apiVersion),
                    new Param("FriendlyName", friendlyName),
                    new Param("SmsApplicationSid", smsApplicationSid),
                    new Param("SmsFallbackMethod", smsFallbackMethod),
                    new Param("SmsFallbackUrl", smsFallbackUrl),
                    new Param("SmsMethod", smsMethod),
                    new Param("SmsUrl", smsUrl),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("VoiceApplicationSid", voiceApplicationSid),
                    new Param("VoiceCallerIdLookup", voiceCallerIdLookup),
                    new Param("VoiceFallbackMethod", voiceFallbackMethod),
                    new Param("VoiceFallbackUrl", voiceFallbackUrl),
                    new Param("VoiceMethod", voiceMethod),
                    new Param("VoiceUrl", voiceUrl),
                    new Param("IdentitySid", identitySid),
                    new Param("AddressSid", addressSid),
                    new Param("EmergencyStatus", emergencyStatus),
                    new Param("EmergencyAddressSid", emergencyAddressSid),
                    new Param("TrunkSid", trunkSid),
                    new Param("VoiceReceiveMode", voiceReceiveMode),
                    new Param("BundleSid", bundleSid)]),
            JsonResponse.Create<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberLocal>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Incoming local phone numbers on a Twilio account/project
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resources to read.</param>
    /// <param name="beta">Whether to include phone numbers new to the Twilio platform. Can be: <c>true</c> or <c>false</c> and the default is <c>true</c>.</param>
    /// <param name="friendlyName">A string that identifies the resources to read.</param>
    /// <param name="phoneNumber">The phone numbers of the IncomingPhoneNumber resources to read. You can specify partial numbers and use '*' as a wildcard for any digit.</param>
    /// <param name="origin">Whether to include phone numbers based on their origin. Can be: <c>twilio</c> or <c>hosted</c>. By default, phone numbers of all origin are included.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListIncomingPhoneNumberLocalResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListIncomingPhoneNumberLocalResponse> ListIncomingPhoneNumberLocal(string accountSid,
        bool? beta,
        string? friendlyName,
        string? phoneNumber,
        string? origin,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/Local.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("Beta", beta),
                new Param("FriendlyName", friendlyName),
                new Param("PhoneNumber", phoneNumber),
                new Param("Origin", origin),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListIncomingPhoneNumberLocalResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
