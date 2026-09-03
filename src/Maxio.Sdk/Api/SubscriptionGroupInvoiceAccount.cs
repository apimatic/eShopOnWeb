using System;
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

public sealed class SubscriptionGroupInvoiceAccount
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionGroupInvoiceAccount(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Subscription Group Prepayment
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionGroupPrepaymentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSubscriptionGroupPrepaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Adds a prepayment for a subscription group. This endpoint requires an <c>amount</c>, <c>details</c>, <c>method</c>, and <c>memo</c>. On success, the prepayment will be added to the group's prepayment balance.
    /// </remarks>
    public Task<SubscriptionGroupPrepaymentResponse> CreateSubscriptionGroupPrepayment(string uid,
        SubscriptionGroupPrepaymentRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/prepayments.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionGroupPrepaymentResponse>(),
            CreateSubscriptionGroupPrepaymentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Deduct Subscription Group Service Credit
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ServiceCredit"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeductSubscriptionGroupServiceCreditError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deducts service credit for a subscription group. Credit will be deducted from the group in the amount specified in the request body.
    /// </remarks>
    public Task<ServiceCredit> DeductSubscriptionGroupServiceCredit(string uid,
        DeductServiceCreditRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/service_credit_deductions.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ServiceCredit>(),
            DeductSubscriptionGroupServiceCreditErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Issue Subscription Group Service Credit
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ServiceCreditResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="IssueSubscriptionGroupServiceCreditError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Issues service credit for a subscription group. Credit will be added to the group in the amount specified in the request body. The credit will be applied to group member invoices as they are generated.
    /// </remarks>
    public Task<ServiceCreditResponse> IssueSubscriptionGroupServiceCredit(string uid,
        IssueServiceCreditRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/service_credits.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ServiceCreditResponse>(),
            IssueSubscriptionGroupServiceCreditErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Prepayments For Subscription Group
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="filter">Filter to use for List Prepayments operations</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSubscriptionGroupPrepaymentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListPrepaymentsForSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists a subscription group's prepayments.
    /// </remarks>
    public Task<ListSubscriptionGroupPrepaymentResponse> ListPrepaymentsForSubscriptionGroup(string uid,
        ListPrepaymentsFilter? filter,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/prepayments.json"),
            [new TemplateParam("uid", uid)],
            [new Param("page", page), new Param("per_page", perPage), new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSubscriptionGroupPrepaymentResponse>(),
            ListPrepaymentsForSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
