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

public sealed class Api20100401ShortCode
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401ShortCode(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch an instance of a short code
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the ShortCode resource(s) to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the ShortCode resource to fetch</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountShortCode"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a short code
    /// </remarks>
    public Task<ApiV2010AccountShortCode> FetchShortCode(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SMS/ShortCodes/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountShortCode>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of short-codes belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the ShortCode resource(s) to read.</param>
    /// <param name="friendlyName">The string that identifies the ShortCode resources to read.</param>
    /// <param name="shortCode">Only show the ShortCode resources that match this pattern. You can specify partial numbers and use '*' as a wildcard for any digit.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListShortCodeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of short-codes belonging to the account used to make the request
    /// </remarks>
    public Task<ListShortCodeResponse> ListShortCode(string accountSid,
        string? friendlyName,
        string? shortCode,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SMS/ShortCodes.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("FriendlyName", friendlyName),
                new Param("ShortCode", shortCode),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListShortCodeResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a short code with the following parameters
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the ShortCode resource(s) to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the ShortCode resource to update</param>
    /// <param name="friendlyName"></param>
    /// <param name="apiVersion"></param>
    /// <param name="smsUrl"></param>
    /// <param name="smsMethod"></param>
    /// <param name="smsFallbackUrl"></param>
    /// <param name="smsFallbackMethod"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountShortCode"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a short code with the following parameters
    /// </remarks>
    public Task<ApiV2010AccountShortCode> UpdateShortCode(string accountSid,
        string sid,
        string? friendlyName,
        string? apiVersion,
        string? smsUrl,
        SmsMethod14? smsMethod,
        string? smsFallbackUrl,
        SmsFallbackMethod14? smsFallbackMethod,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SMS/ShortCodes/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("ApiVersion", apiVersion),
                    new Param("SmsUrl", smsUrl),
                    new Param("SmsMethod", smsMethod),
                    new Param("SmsFallbackUrl", smsFallbackUrl),
                    new Param("SmsFallbackMethod", smsFallbackMethod)]),
            JsonResponse.Create<ApiV2010AccountShortCode>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
