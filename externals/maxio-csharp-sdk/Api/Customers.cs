using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class Customers
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Customers(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Customer
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateCustomerError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new customer; can also be created alongside a new subscription. The only validation restriction is that you may only create one customer for a given reference value.
    /// <para>
    /// If provided, the <c>reference</c> value must be unique. It represents a unique identifier for the customer from your own app, i.e. the customer’s ID. This allows you to retrieve a given customer via a piece of shared information. Alternatively, you may choose to leave <c>reference</c> blank, and store Advanced Billing’s unique ID for the customer, which is in the <c>id</c> attribute.
    /// </para>
    /// <para>
    /// Full documentation on how to locate, create and edit Customers in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24252190590093-Customer-Details">here</see>.
    /// </para>
    /// <para>
    /// ## Required Country Format
    /// </para>
    /// <para>
    /// Advanced Billing requires that you use the ISO Standard Country codes when formatting country attribute of the customer.
    /// </para>
    /// <para>
    /// Countries should be formatted as 2 characters. For more information, see the following wikipedia article on <see href="http://en.wikipedia.org/wiki/ISO_3166-1#Current_codes">ISO_3166-1.</see>
    /// </para>
    /// <para>
    /// ## Required State Format
    /// </para>
    /// <para>
    /// Advanced Billing requires that you use the ISO Standard State codes when formatting state attribute of the customer.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>US States (2 characters): <see href="https://en.wikipedia.org/wiki/ISO_3166-2:US">ISO_3166-2</see></description></item>
    /// </list>
    /// <list type="bullet">
    ///   <item><description>States Outside the US (2-3 characters): To find the correct state codes outside of the US, go to <see href="http://en.wikipedia.org/wiki/ISO_3166-1#Current_codes">ISO_3166-1</see> and click on the link in the “ISO 3166-2 codes” column next to country you wish to populate.</description></item>
    /// </list>
    /// <para>
    /// ## Locale
    /// </para>
    /// <para>
    /// Advanced Billing allows you to attribute a language/region to your customer to deliver invoices in any required language.
    /// For more: <see href="https://maxio.zendesk.com/hc/en-us/articles/24286672013709-Customer-Locale">Customer Locale</see>
    /// </para>
    /// </remarks>
    public Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CustomerResponse>(),
            CreateCustomerErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Customer
    /// </summary>
    /// <param name="id">The Advanced Billing id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes the customer.
    /// </remarks>
    public Task DeleteCustomer(int id, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers/{id}.json"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Customer Subscriptions
    /// </summary>
    /// <param name="customerId">The Chargify id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="SubscriptionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists all subscriptions that belong to a customer.
    /// <para>
    ///  If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, subscriptions no longer require an associated product. For subscriptions without an associated product, 'product', 'product_price_point_id', and 'product_price_point_type' are returned as 'null'.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers/{customer_id}/subscriptions.json"),
            [new TemplateParam("customer_id", customerId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<SubscriptionResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List or Find Customers
    /// </summary>
    /// <param name="direction">Direction to sort customers by time of creation</param>
    /// <param name="dateField">The type of filter you would like to apply to your search. Use in query: <c>date_field=created_at</c>.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns subscriptions with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns subscriptions with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns subscriptions with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns subscriptions with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="q">A search query by which to filter customers (can be an email, an ID, a reference, organization)</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 50. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists all customers associated with your site, or filters results using the search parameter.
    /// <para>
    /// ## Find Customer
    /// </para>
    /// <para>
    /// Use the search feature with the <c>q</c> query parameter to retrieve an array of customers that matches the search query.
    /// </para>
    /// <para>
    /// Common use cases are:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Search by an email</description></item>
    ///   <item><description>Search by an Advanced Billing ID</description></item>
    ///   <item><description>Search by an organization</description></item>
    ///   <item><description>Search by a reference value from your application</description></item>
    ///   <item><description>Search by a first or last name</description></item>
    /// </list>
    /// <para>
    /// To retrieve a single, exact match by reference, use the <see href="https://developers.chargify.com/docs/api-docs/b710d8fbef104-read-customer-by-reference">lookup endpoint</see>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<CustomerResponse>> ListCustomers(SortingDirection? direction,
        BasicDateField? dateField,
        string? startDate,
        string? endDate,
        string? startDatetime,
        string? endDatetime,
        string? q,
        int? page = 1,
        int? perPage = 50,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers.json"),
            [],
            [new Param("direction", direction),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("date_field", dateField),
                new Param("start_date", startDate),
                new Param("end_date", endDate),
                new Param("start_datetime", startDatetime),
                new Param("end_datetime", endDatetime),
                new Param("q", q)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<CustomerResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Customer
    /// </summary>
    /// <param name="id">The Advanced Billing id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves the Customer properties by Advanced Billing-generated Customer ID.
    /// </remarks>
    public Task<CustomerResponse> ReadCustomer(int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers/{id}.json"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CustomerResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Customer by Reference
    /// </summary>
    /// <param name="reference">Customer reference</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a customer by their unique reference ID. It will return a single match.
    /// </remarks>
    public Task<CustomerResponse> ReadCustomerByReference(string reference,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers/lookup.json"),
            [],
            [new Param("reference", reference)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CustomerResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Customer
    /// </summary>
    /// <param name="id">The Advanced Billing id of the customer</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateCustomerError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the customer.
    /// </remarks>
    public Task<CustomerResponse> UpdateCustomer(int id,
        UpdateCustomerRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/customers/{id}.json"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<CustomerResponse>(),
            UpdateCustomerErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
