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

public sealed class TrusthubV1CustomerProfilesEntityAssignments
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1CustomerProfilesEntityAssignments(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Assigned Item.
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="objectSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfileCustomerProfileEntityAssignment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Assigned Item.
    /// </remarks>
    public Task<TrusthubV1CustomerProfileCustomerProfileEntityAssignment> CreateCustomerProfileEntityAssignment(string customerProfileSid,
        string objectSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/EntityAssignments"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("ObjectSid", objectSid)]),
            JsonResponse.Create<TrusthubV1CustomerProfileCustomerProfileEntityAssignment>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove an Assignment Item Instance.
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="sid">The unique string that we created to identify the Identity resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove an Assignment Item Instance.
    /// </remarks>
    public Task DeleteCustomerProfileEntityAssignment(string customerProfileSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/EntityAssignments/{Sid}"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid), new TemplateParam("Sid", sid)],
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
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="sid">The unique string that we created to identify the Identity resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfileCustomerProfileEntityAssignment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Assigned Item Instance.
    /// </remarks>
    public Task<TrusthubV1CustomerProfileCustomerProfileEntityAssignment> FetchCustomerProfileEntityAssignment(string customerProfileSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/EntityAssignments/{Sid}"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TrusthubV1CustomerProfileCustomerProfileEntityAssignment>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Assigned Items for an account.
    /// </summary>
    /// <param name="customerProfileSid">The unique string that we created to identify the CustomerProfile resource.</param>
    /// <param name="objectType">A string to filter the results by (EndUserType or SupportingDocumentType) machine-name. This is useful when you want to retrieve the entity-assignment of a specific end-user or supporting document.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCustomerProfileEntityAssignmentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Assigned Items for an account.
    /// </remarks>
    public Task<ListCustomerProfileEntityAssignmentResponse> ListCustomerProfileEntityAssignment(string customerProfileSid,
        string? objectType,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{CustomerProfileSid}/EntityAssignments"),
            [new TemplateParam("CustomerProfileSid", customerProfileSid)],
            [new Param("ObjectType", objectType),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCustomerProfileEntityAssignmentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
