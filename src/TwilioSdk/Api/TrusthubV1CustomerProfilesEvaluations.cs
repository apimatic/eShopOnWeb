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

namespace TwilioSdk.Api;

public sealed class TrusthubV1CustomerProfilesEvaluations
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1CustomerProfilesEvaluations(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Evaluation
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="policySid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfileCustomerProfileEvaluation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Evaluation
    /// </remarks>
    public Task<TrusthubV1CustomerProfileCustomerProfileEvaluation> CreateCustomerProfileEvaluation(string customerProfileSid,
        string policySid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/Evaluations"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PolicySid", policySid)]),
            JsonResponse.Create<TrusthubV1CustomerProfileCustomerProfileEvaluation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch specific Evaluation Instance.
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the customer_profile resource.</param>
    /// <param name="sid">The unique string that identifies the Evaluation resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfileCustomerProfileEvaluation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Evaluation Instance.
    /// </remarks>
    public Task<TrusthubV1CustomerProfileCustomerProfileEvaluation> FetchCustomerProfileEvaluation(string customerProfileSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/Evaluations/{Sid}"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TrusthubV1CustomerProfileCustomerProfileEvaluation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of Evaluations associated to the customer_profile resource.
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCustomerProfileEvaluationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Evaluations associated to the customer_profile resource.
    /// </remarks>
    public Task<ListCustomerProfileEvaluationResponse> ListCustomerProfileEvaluation(string customerProfileSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/Evaluations"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCustomerProfileEvaluationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
