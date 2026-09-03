using System;
using System.Collections.Generic;
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

public sealed class NumbersV2AuthorizationDocumentApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2AuthorizationDocumentApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create an AuthorizationDocument for authorizing the hosting of phone number capabilities on Twilio's platform.
    /// </summary>
    /// <param name="addressSid"></param>
    /// <param name="email"></param>
    /// <param name="contactPhoneNumber"></param>
    /// <param name="hostedNumberOrderSids"></param>
    /// <param name="contactTitle"></param>
    /// <param name="ccEmails"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2AuthorizationDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create an AuthorizationDocument for authorizing the hosting of phone number capabilities on Twilio's platform.
    /// </remarks>
    public Task<NumbersV2AuthorizationDocument> CreateAuthorizationDocument(string addressSid,
        string email,
        string contactPhoneNumber,
        IReadOnlyList<string> hostedNumberOrderSids,
        string? contactTitle,
        IReadOnlyList<string>? ccEmails,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/AuthorizationDocuments"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("AddressSid", addressSid),
                    new Param("Email", email),
                    new Param("ContactPhoneNumber", contactPhoneNumber),
                    new Param("HostedNumberOrderSids", hostedNumberOrderSids),
                    new Param("ContactTitle", contactTitle),
                    new Param("CcEmails", ccEmails)]),
            JsonResponse.Create<NumbersV2AuthorizationDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Cancel the AuthorizationDocument request.
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this AuthorizationDocument.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancel the AuthorizationDocument request.
    /// </remarks>
    public Task DeleteAuthorizationDocument(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/AuthorizationDocuments/{Sid}"),
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
    /// Fetch a specific AuthorizationDocument.
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this AuthorizationDocument.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2AuthorizationDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific AuthorizationDocument.
    /// </remarks>
    public Task<NumbersV2AuthorizationDocument> FetchAuthorizationDocument(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/AuthorizationDocuments/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV2AuthorizationDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of AuthorizationDocuments belonging to the account initiating the request.
    /// </summary>
    /// <param name="email">Email that this AuthorizationDocument will be sent to for signing.</param>
    /// <param name="status">Status of an instance resource. It can hold one of the values: 1. opened 2. signing, 3. signed LOA, 4. canceled, 5. failed. See the section entitled <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource#status-values">Status Values</see> for more information on each of these statuses.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListAuthorizationDocumentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of AuthorizationDocuments belonging to the account initiating the request.
    /// </remarks>
    public Task<ListAuthorizationDocumentResponse> ListAuthorizationDocument(string? email,
        AuthorizationDocumentEnumStatus? status,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/AuthorizationDocuments"),
            [],
            [new Param("Email", email),
                new Param("Status", status),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListAuthorizationDocumentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
