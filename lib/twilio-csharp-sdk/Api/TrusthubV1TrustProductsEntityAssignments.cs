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

public sealed class TrusthubV1TrustProductsEntityAssignments
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1TrustProductsEntityAssignments(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Assigned Item.
    /// </summary>
    /// <param name="trustProductSid">The unique string that we created to identify the TrustProduct resource.</param>
    /// <param name="objectSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1TrustProductTrustProductEntityAssignment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Assigned Item.
    /// </remarks>
    public Task<TrusthubV1TrustProductTrustProductEntityAssignment> CreateTrustProductEntityAssignment(string trustProductSid,
        string objectSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/TrustProducts/{TrustProductSid}/EntityAssignments"),
            [new TemplateParam("TrustProductSid", trustProductSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("ObjectSid", objectSid)]),
            JsonResponse.Create<TrusthubV1TrustProductTrustProductEntityAssignment>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove an Assignment Item Instance.
    /// </summary>
    /// <param name="trustProductSid">The unique string that we created to identify the TrustProduct resource.</param>
    /// <param name="sid">The unique string that we created to identify the Identity resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove an Assignment Item Instance.
    /// </remarks>
    public Task DeleteTrustProductEntityAssignment(string trustProductSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/TrustProducts/{TrustProductSid}/EntityAssignments/{Sid}"),
            [new TemplateParam("TrustProductSid", trustProductSid), new TemplateParam("Sid", sid)],
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
    /// Fetch specific Assigned Item Instance.
    /// </summary>
    /// <param name="trustProductSid">The unique string that we created to identify the TrustProduct resource.</param>
    /// <param name="sid">The unique string that we created to identify the Identity resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1TrustProductTrustProductEntityAssignment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Assigned Item Instance.
    /// </remarks>
    public Task<TrusthubV1TrustProductTrustProductEntityAssignment> FetchTrustProductEntityAssignment(string trustProductSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/TrustProducts/{TrustProductSid}/EntityAssignments/{Sid}"),
            [new TemplateParam("TrustProductSid", trustProductSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TrusthubV1TrustProductTrustProductEntityAssignment>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Assigned Items for an account.
    /// </summary>
    /// <param name="trustProductSid">The unique string that we created to identify the TrustProduct resource.</param>
    /// <param name="objectType">A string to filter the results by (EndUserType or SupportingDocumentType) machine-name. This is useful when you want to retrieve the entity-assignment of a specific end-user or supporting document.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListTrustProductEntityAssignmentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Assigned Items for an account.
    /// </remarks>
    public Task<ListTrustProductEntityAssignmentResponse> ListTrustProductEntityAssignment(string trustProductSid,
        string? objectType,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/TrustProducts/{TrustProductSid}/EntityAssignments"),
            [new TemplateParam("TrustProductSid", trustProductSid)],
            [new Param("ObjectType", objectType),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTrustProductEntityAssignmentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
