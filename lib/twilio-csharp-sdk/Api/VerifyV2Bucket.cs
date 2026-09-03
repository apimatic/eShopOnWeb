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

namespace Twilio.Api;

public sealed class VerifyV2Bucket
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2Bucket(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Bucket for a Rate Limit
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.</param>
    /// <param name="rateLimitSid">The Twilio-provided string that uniquely identifies the Rate Limit resource.</param>
    /// <param name="max"></param>
    /// <param name="interval"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceRateLimitBucket"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Bucket for a Rate Limit
    /// </remarks>
    public Task<VerifyV2ServiceRateLimitBucket> CreateBucket(string serviceSid,
        string rateLimitSid,
        int max,
        int interval,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/RateLimits/{RateLimitSid}/Buckets"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("RateLimitSid", rateLimitSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Max", max), new Param("Interval", interval)]),
            JsonResponse.Create<VerifyV2ServiceRateLimitBucket>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Bucket.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.</param>
    /// <param name="rateLimitSid">The Twilio-provided string that uniquely identifies the Rate Limit resource.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this Bucket.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Bucket.
    /// </remarks>
    public Task DeleteBucket(string serviceSid,
        string rateLimitSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/RateLimits/{RateLimitSid}/Buckets/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("RateLimitSid", rateLimitSid),
                new TemplateParam("Sid", sid)],
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
    /// Fetch a specific Bucket.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.</param>
    /// <param name="rateLimitSid">The Twilio-provided string that uniquely identifies the Rate Limit resource.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this Bucket.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceRateLimitBucket"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Bucket.
    /// </remarks>
    public Task<VerifyV2ServiceRateLimitBucket> FetchBucket(string serviceSid,
        string rateLimitSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/RateLimits/{RateLimitSid}/Buckets/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("RateLimitSid", rateLimitSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2ServiceRateLimitBucket>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Buckets for a Rate Limit.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.</param>
    /// <param name="rateLimitSid">The Twilio-provided string that uniquely identifies the Rate Limit resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListBucketResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Buckets for a Rate Limit.
    /// </remarks>
    public Task<ListBucketResponse> ListBucket(string serviceSid,
        string rateLimitSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/RateLimits/{RateLimitSid}/Buckets"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("RateLimitSid", rateLimitSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListBucketResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Bucket.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.</param>
    /// <param name="rateLimitSid">The Twilio-provided string that uniquely identifies the Rate Limit resource.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this Bucket.</param>
    /// <param name="max"></param>
    /// <param name="interval"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceRateLimitBucket"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Bucket.
    /// </remarks>
    public Task<VerifyV2ServiceRateLimitBucket> UpdateBucket(string serviceSid,
        string rateLimitSid,
        string sid,
        int? max,
        int? interval,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/RateLimits/{RateLimitSid}/Buckets/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("RateLimitSid", rateLimitSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Max", max), new Param("Interval", interval)]),
            JsonResponse.Create<VerifyV2ServiceRateLimitBucket>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
