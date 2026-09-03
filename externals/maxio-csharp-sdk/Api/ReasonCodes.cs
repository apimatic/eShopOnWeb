using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

public sealed class ReasonCodes
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ReasonCodes(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Reason Code
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReasonCodeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateReasonCodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a reason code for a given site.
    /// <para>
    /// # Reason Codes Intro
    /// </para>
    /// <para>
    /// Reason Codes are a way to gain a high-level view of why your customers are cancelling the subscription to your product or service.
    /// </para>
    /// <para>
    /// Add a set of churn reason codes to be displayed in-app and/or the Maxio Billing Portal. As your subscribers decide to cancel their subscription, learn why they decided to cancel.
    /// </para>
    /// <para>
    /// ## Reason Code Documentation
    /// </para>
    /// <para>
    /// Full documentation on how Reason Codes operate within Advanced Billing can be located under the following links.
    /// </para>
    /// <para>
    /// <see href="https://maxio.zendesk.com/hc/en-us/articles/24286647554701-Churn-Reason-Codes">Churn Reason Codes</see>
    /// </para>
    /// <para>
    /// ## Create Reason Code
    /// </para>
    /// <para>
    /// This method gives a merchant the option to create reason codes for a given site.
    /// </para>
    /// </remarks>
    public Task<ReasonCodeResponse> CreateReasonCode(CreateReasonCodeRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/reason_codes.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ReasonCodeResponse>(),
            CreateReasonCodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Reason Code
    /// </summary>
    /// <param name="reasonCodeId">The Advanced Billing id of the reason code</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OkResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteReasonCodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a reason code from the Churn Reason Codes. This code will be immediately removed. This action is not reversible.
    /// </remarks>
    public Task<OkResponse> DeleteReasonCode(int reasonCodeId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/reason_codes/{reason_code_id}.json"),
            [new TemplateParam("reason_code_id", reasonCodeId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<OkResponse>(),
            DeleteReasonCodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Reason Codes
    /// </summary>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ReasonCodeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListReasonCodesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists all current churn codes for a given site.
    /// </remarks>
    public Task<IReadOnlyList<ReasonCodeResponse>> ListReasonCodes(int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/reason_codes.json"),
            [],
            [new Param("page", page), new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ReasonCodeResponse>>(),
            ListReasonCodesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Reason Code
    /// </summary>
    /// <param name="reasonCodeId">The Advanced Billing id of the reason code</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReasonCodeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadReasonCodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a particular churn reason code for a given site by its unique ID.
    /// </remarks>
    public Task<ReasonCodeResponse> ReadReasonCode(int reasonCodeId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/reason_codes/{reason_code_id}.json"),
            [new TemplateParam("reason_code_id", reasonCodeId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ReasonCodeResponse>(),
            ReadReasonCodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Reason Code
    /// </summary>
    /// <param name="reasonCodeId">The Advanced Billing id of the reason code</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReasonCodeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateReasonCodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an existing reason code for a given site.
    /// </remarks>
    public Task<ReasonCodeResponse> UpdateReasonCode(int reasonCodeId,
        UpdateReasonCodeRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/reason_codes/{reason_code_id}.json"),
            [new TemplateParam("reason_code_id", reasonCodeId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ReasonCodeResponse>(),
            UpdateReasonCodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
