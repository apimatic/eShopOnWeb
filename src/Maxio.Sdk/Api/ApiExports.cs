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

public sealed class ApiExports
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ApiExports(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Invoices Export
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ExportInvoicesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an invoices export and returns a batch job object.
    /// </remarks>
    public Task<BatchJobResponse> ExportInvoices(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/invoices.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ExportInvoicesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Proforma Invoices Export
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ExportProformaInvoicesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a proforma invoices export and returns a batch job object.
    /// <para>
    /// It is only available for Relationship Invoicing architecture.
    /// </para>
    /// </remarks>
    public Task<BatchJobResponse> ExportProformaInvoices(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/proforma_invoices.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ExportProformaInvoicesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Subscriptions Export
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ExportSubscriptionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a subscriptions export and returns a batch job object.
    /// </remarks>
    public Task<BatchJobResponse> ExportSubscriptions(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/subscriptions.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ExportSubscriptionsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Exported Invoices
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request.  Default value is 100.  The maximum allowed values is 10000; any per_page value over 10000 will be changed to 10000.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Invoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListExportedInvoicesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists exported invoices for a provided <c>batch_id</c>. Use pagination to control responses returned from the server.
    /// <para>
    /// Example: <c>GET https://{subdomain}.chargify.com/api_exports/invoices/123/rows?per_page=10000&amp;page=1</c>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Invoice>> ListExportedInvoices(string batchId,
        int? perPage = 100,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/invoices/{batch_id}/rows.json"),
            [new TemplateParam("batch_id", batchId)],
            [new Param("per_page", perPage), new Param("page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<Invoice>>(),
            ListExportedInvoicesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Exported Proforma Invoices
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request.  Default value is 100.  The maximum allowed values is 10000; any per_page value over 10000 will be changed to 10000.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListExportedProformaInvoicesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists exported proforma invoices for a provided <c>batch_id</c>. Use pagination to control responses returned from the server.
    /// <para>
    /// Example: <c>GET https://{subdomain}.chargify.com/api_exports/proforma_invoices/123/rows?per_page=10000&amp;page=1</c>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<ProformaInvoice>> ListExportedProformaInvoices(string batchId,
        int? perPage = 100,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/proforma_invoices/{batch_id}/rows.json"),
            [new TemplateParam("batch_id", batchId)],
            [new Param("per_page", perPage), new Param("page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ProformaInvoice>>(),
            ListExportedProformaInvoicesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Exported Subscriptions
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request.  Default value is 100.  The maximum allowed values is 10000; any per_page value over 10000 will be changed to 10000.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Subscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListExportedSubscriptionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists exported subscriptions for a provided <c>batch_id</c>. Use pagination to control responses returned from the server.
    /// <para>
    /// Example: <c>GET https://{subdomain}.chargify.com/api_exports/subscriptions/123/rows?per_page=200&amp;page=1</c>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Subscription>> ListExportedSubscriptions(string batchId,
        int? perPage = 100,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/subscriptions/{batch_id}/rows.json"),
            [new TemplateParam("batch_id", batchId)],
            [new Param("per_page", perPage), new Param("page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<Subscription>>(),
            ListExportedSubscriptionsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Invoices Export
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadInvoicesExportError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a batch job object for an invoices export.
    /// </remarks>
    public Task<BatchJobResponse> ReadInvoicesExport(string batchId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/invoices/{batch_id}.json"),
            [new TemplateParam("batch_id", batchId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ReadInvoicesExportErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Proforma Invoices Export
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadProformaInvoicesExportError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a batch job object for a proforma invoices export.
    /// </remarks>
    public Task<BatchJobResponse> ReadProformaInvoicesExport(string batchId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/proforma_invoices/{batch_id}.json"),
            [new TemplateParam("batch_id", batchId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ReadProformaInvoicesExportErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Subscriptions Export
    /// </summary>
    /// <param name="batchId">Id of a Batch Job.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchJobResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadSubscriptionsExportError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a batch job object for a subscriptions export.
    /// </remarks>
    public Task<BatchJobResponse> ReadSubscriptionsExport(string batchId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/api_exports/subscriptions/{batch_id}.json"),
            [new TemplateParam("batch_id", batchId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BatchJobResponse>(),
            ReadSubscriptionsExportErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
