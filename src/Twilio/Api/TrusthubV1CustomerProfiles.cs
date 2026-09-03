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

public sealed class TrusthubV1CustomerProfiles
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1CustomerProfiles(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Customer-Profile.
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="email"></param>
    /// <param name="policySid"></param>
    /// <param name="statusCallback"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfile"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Customer-Profile.
    /// </remarks>
    public Task<TrusthubV1CustomerProfile> CreateCustomerProfile(string friendlyName,
        string email,
        string policySid,
        string? statusCallback,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Email", email),
                    new Param("PolicySid", policySid),
                    new Param("StatusCallback", statusCallback)]),
            JsonResponse.Create<TrusthubV1CustomerProfile>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Customer-Profile.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Customer-Profile resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Customer-Profile.
    /// </remarks>
    public Task DeleteCustomerProfile(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Fetch a specific Customer-Profile instance.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Customer-Profile resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfile"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Customer-Profile instance.
    /// </remarks>
    public Task<TrusthubV1CustomerProfile> FetchCustomerProfile(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TrusthubV1CustomerProfile>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Customer-Profiles for an account.
    /// </summary>
    /// <param name="status">The verification status of the Customer-Profile resource.</param>
    /// <param name="friendlyName">The string that you assigned to describe the resource.</param>
    /// <param name="policySid">The unique string of a policy that is associated to the Customer-Profile resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCustomerProfileResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Customer-Profiles for an account.
    /// </remarks>
    public Task<ListCustomerProfileResponse> ListCustomerProfile(CustomerProfileEnumStatus? status,
        string? friendlyName,
        string? policySid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles"),
            [],
            [new Param("Status", status),
                new Param("FriendlyName", friendlyName),
                new Param("PolicySid", policySid),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCustomerProfileResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Updates a Customer-Profile in an account.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Customer-Profile resource.</param>
    /// <param name="status"></param>
    /// <param name="statusCallback"></param>
    /// <param name="friendlyName"></param>
    /// <param name="email"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1CustomerProfile"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a Customer-Profile in an account.
    /// </remarks>
    public Task<TrusthubV1CustomerProfile> UpdateCustomerProfile(string sid,
        CustomerProfileEnumStatus? status,
        string? statusCallback,
        string? friendlyName,
        string? email,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/CustomerProfiles/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status),
                    new Param("StatusCallback", statusCallback),
                    new Param("FriendlyName", friendlyName),
                    new Param("Email", email)]),
            JsonResponse.Create<TrusthubV1CustomerProfile>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
