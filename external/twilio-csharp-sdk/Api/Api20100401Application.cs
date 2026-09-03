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

public sealed class Api20100401Application
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Application(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new application within your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="apiVersion"></param>
    /// <param name="voiceUrl"></param>
    /// <param name="voiceMethod"></param>
    /// <param name="voiceFallbackUrl"></param>
    /// <param name="voiceFallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="voiceCallerIdLookup"></param>
    /// <param name="smsUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="smsStatusCallback"></param>
    /// <param name="messageStatusCallback"></param>
    /// <param name="friendlyName"></param>
    /// <param name="publicApplicationConnectEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountApplication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new application within your account
    /// </remarks>
    public Task<ApiV2010AccountApplication> CreateApplication(string accountSid,
        string? apiVersion,
        string? voiceUrl,
        VoiceMethod7? voiceMethod,
        string? voiceFallbackUrl,
        VoiceFallbackMethod7? voiceFallbackMethod,
        string? statusCallback,
        StatusCallbackMethod6? statusCallbackMethod,
        bool? voiceCallerIdLookup,
        string? smsUrl,
        SmsMethod7? smsMethod,
        string? smsFallbackUrl,
        SmsFallbackMethod7? smsFallbackMethod,
        string? smsStatusCallback,
        string? messageStatusCallback,
        string? friendlyName,
        bool? publicApplicationConnectEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Applications.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("ApiVersion", apiVersion),
                    new Param("VoiceUrl", voiceUrl),
                    new Param("VoiceMethod", voiceMethod),
                    new Param("VoiceFallbackUrl", voiceFallbackUrl),
                    new Param("VoiceFallbackMethod", voiceFallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("VoiceCallerIdLookup", voiceCallerIdLookup),
                    new Param("SmsUrl", smsUrl),
                    new Param("SmsMethod", smsMethod),
                    new Param("SmsFallbackUrl", smsFallbackUrl),
                    new Param("SmsFallbackMethod", smsFallbackMethod),
                    new Param("SmsStatusCallback", smsStatusCallback),
                    new Param("MessageStatusCallback", messageStatusCallback),
                    new Param("FriendlyName", friendlyName),
                    new Param("PublicApplicationConnectEnabled", publicApplicationConnectEnabled)]),
            JsonResponse.Create<ApiV2010AccountApplication>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete the application by the specified application sid
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Application resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Application resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete the application by the specified application sid
    /// </remarks>
    public Task DeleteApplication(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Applications/{Sid}.json"),
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
    /// Fetch the application specified by the provided sid
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Application resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Application resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountApplication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the application specified by the provided sid
    /// </remarks>
    public Task<ApiV2010AccountApplication> FetchApplication(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Applications/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountApplication>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of applications representing an application within the requesting account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Application resources to read.</param>
    /// <param name="friendlyName">The string that identifies the Application resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListApplicationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of applications representing an application within the requesting account
    /// </remarks>
    public Task<ListApplicationResponse> ListApplication(string accountSid,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Applications.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListApplicationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Updates the application's properties
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Application resources to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Application resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="apiVersion"></param>
    /// <param name="voiceUrl"></param>
    /// <param name="voiceMethod"></param>
    /// <param name="voiceFallbackUrl"></param>
    /// <param name="voiceFallbackMethod"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="voiceCallerIdLookup"></param>
    /// <param name="smsUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="smsStatusCallback"></param>
    /// <param name="messageStatusCallback"></param>
    /// <param name="publicApplicationConnectEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountApplication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the application's properties
    /// </remarks>
    public Task<ApiV2010AccountApplication> UpdateApplication(string accountSid,
        string sid,
        string? friendlyName,
        string? apiVersion,
        string? voiceUrl,
        VoiceMethod7? voiceMethod,
        string? voiceFallbackUrl,
        VoiceFallbackMethod7? voiceFallbackMethod,
        string? statusCallback,
        StatusCallbackMethod6? statusCallbackMethod,
        bool? voiceCallerIdLookup,
        string? smsUrl,
        SmsMethod7? smsMethod,
        string? smsFallbackUrl,
        SmsFallbackMethod7? smsFallbackMethod,
        string? smsStatusCallback,
        string? messageStatusCallback,
        bool? publicApplicationConnectEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Applications/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("ApiVersion", apiVersion),
                    new Param("VoiceUrl", voiceUrl),
                    new Param("VoiceMethod", voiceMethod),
                    new Param("VoiceFallbackUrl", voiceFallbackUrl),
                    new Param("VoiceFallbackMethod", voiceFallbackMethod),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("VoiceCallerIdLookup", voiceCallerIdLookup),
                    new Param("SmsUrl", smsUrl),
                    new Param("SmsMethod", smsMethod),
                    new Param("SmsFallbackUrl", smsFallbackUrl),
                    new Param("SmsFallbackMethod", smsFallbackMethod),
                    new Param("SmsStatusCallback", smsStatusCallback),
                    new Param("MessageStatusCallback", messageStatusCallback),
                    new Param("PublicApplicationConnectEnabled", publicApplicationConnectEnabled)]),
            JsonResponse.Create<ApiV2010AccountApplication>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
