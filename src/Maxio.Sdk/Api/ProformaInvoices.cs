using System;
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

public sealed class ProformaInvoices
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProformaInvoices(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Consolidated Proforma Invoices
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateConsolidatedProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a consolidated proforma invoice asynchronously. It will return a 201 with no message, or a 422 with any errors. To find and view the new consolidated proforma invoice, you may poll the subscription group listing for proforma invoices; only one consolidated proforma invoice may be created per group at a time.
    /// <para>
    /// If the information becomes outdated, simply void the old consolidated proforma invoice and generate a new one.
    /// </para>
    /// <para>
    /// ## Restrictions
    /// </para>
    /// <para>
    /// Proforma invoices are only available on Relationship Invoicing sites. To create a proforma invoice, the subscription must not be prepaid, and must be in a live state.
    /// </para>
    /// </remarks>
    public Task CreateConsolidatedProformaInvoice(string uid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/proforma_invoices.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            CreateConsolidatedProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Proforma Invoice
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a proforma invoice and returns it as a response. If the information becomes outdated, simply void the old proforma invoice and generate a new one.
    /// <para>
    /// If you would like to preview the next billing amounts without generating a full proforma invoice, use the renewal preview endpoint.
    /// </para>
    /// <para>
    /// ## Restrictions
    /// </para>
    /// <para>
    /// Proforma invoices are only available on Relationship Invoicing sites. To create a proforma invoice, the subscription must not be in a group, must not be prepaid, and must be in a live state.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> CreateProformaInvoice(int subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/proforma_invoices.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<ProformaInvoice>(),
            CreateProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create signup proforma invoice
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSignupProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a proforma invoice to preview costs before a subscription's signup. This endpoint is only available for Relationship Invoicing sites and cannot be used to create consolidated proforma invoices or preview prepaid subscriptions. Like other proforma invoices, it can be emailed to the customer, voided, and publicly viewed on the chargifypay domain.
    /// <para>
    /// Pass a payload that resembles a subscription create or signup preview request. For example, you can specify components, coupons/a referral, offers, custom pricing, and an existing customer or payment profile to populate a shipping or billing address.
    /// </para>
    /// <para>
    /// A product and customer first name, last name, and email are the minimum requirements. We recommend associating the proforma invoice with a customer_id to easily find their proforma invoices, since the subscription_id will always be blank.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> CreateSignupProformaInvoice(CreateSubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/proforma_invoices.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProformaInvoice>(),
            CreateSignupProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Deliver Proforma Invoice
    /// </summary>
    /// <param name="proformaInvoiceUid">The uid of the proforma invoice</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeliverProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delivers a proforma invoice programmatically via email. Supports email
    /// delivery to direct recipients, carbon-copy (cc) recipients, and blind carbon-copy (bcc) recipients.
    /// <para>
    /// If <c>recipient_emails</c> is omitted, the system will fall back to the primary recipient derived from the invoice or
    /// subscription. At least one recipient must be present, either via the request body or via this default behavior, so an
    /// empty body may still succeed when defaults are available.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> DeliverProformaInvoice(string proformaInvoiceUid,
        DeliverProformaInvoiceRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/proforma_invoices/{proforma_invoice_uid}/deliveries.json"),
            [new TemplateParam("proforma_invoice_uid", proformaInvoiceUid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProformaInvoice>(),
            DeliverProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Proforma Invoices
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="startDate">The beginning date range for the invoice's Due Date, in the YYYY-MM-DD format.</param>
    /// <param name="endDate">The ending date range for the invoice's Due Date, in the YYYY-MM-DD format.</param>
    /// <param name="status">The current status of the invoice.  Allowed Values: draft, open, paid, pending, voided</param>
    /// <param name="direction">The sort direction of the returned invoices.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="lineItems">Include line items data.</param>
    /// <param name="discounts">Include discounts data.</param>
    /// <param name="taxes">Include taxes data.</param>
    /// <param name="credits">Include credits data.</param>
    /// <param name="payments">Include payments data.</param>
    /// <param name="customFields">Include custom fields data.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListProformaInvoicesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists proforma invoices for a subscription. By default, results only include totals, not detailed breakdowns for <c>line_items</c>, <c>discounts</c>, <c>taxes</c>, <c>credits</c>, <c>payments</c>, or <c>custom_fields</c>. To include breakdowns, pass the specific field as a key in the query with a value set to <c>true</c>.
    /// </remarks>
    public Task<ListProformaInvoicesResponse> ListProformaInvoices(int subscriptionId,
        string? startDate,
        string? endDate,
        ProformaInvoiceStatus? status,
        Direction? direction,
        int? page = 1,
        int? perPage = 20,
        bool? lineItems = false,
        bool? discounts = false,
        bool? taxes = false,
        bool? credits = false,
        bool? payments = false,
        bool? customFields = false,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/proforma_invoices.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [new Param("start_date", startDate),
                new Param("end_date", endDate),
                new Param("status", status),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("direction", direction),
                new Param("line_items", lineItems),
                new Param("discounts", discounts),
                new Param("taxes", taxes),
                new Param("credits", credits),
                new Param("payments", payments),
                new Param("custom_fields", customFields)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListProformaInvoicesResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Group Proforma Invoices
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="lineItems">Include line items data.</param>
    /// <param name="discounts">Include discounts data.</param>
    /// <param name="taxes">Include taxes data.</param>
    /// <param name="credits">Include credits data.</param>
    /// <param name="payments">Include payments data.</param>
    /// <param name="customFields">Include custom fields data.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListProformaInvoicesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListSubscriptionGroupProformaInvoicesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists proforma invoices with a <c>consolidation_level</c> of parent for the subscription group.
    /// <para>
    /// By default, proforma invoices returned on the index will only include totals, not detailed breakdowns for <c>line_items</c>, <c>discounts</c>, <c>taxes</c>, <c>credits</c>, <c>payments</c>, <c>custom_fields</c>. To include breakdowns, pass the specific field as a key in the query with a value set to true.
    /// </para>
    /// </remarks>
    public Task<ListProformaInvoicesResponse> ListSubscriptionGroupProformaInvoices(string uid,
        bool? lineItems = false,
        bool? discounts = false,
        bool? taxes = false,
        bool? credits = false,
        bool? payments = false,
        bool? customFields = false,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/proforma_invoices.json"),
            [new TemplateParam("uid", uid)],
            [new Param("line_items", lineItems),
                new Param("discounts", discounts),
                new Param("taxes", taxes),
                new Param("credits", credits),
                new Param("payments", payments),
                new Param("custom_fields", customFields)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListProformaInvoicesResponse>(),
            ListSubscriptionGroupProformaInvoicesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Preview Proforma Invoice
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PreviewProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Previews the data that will be included on a given subscription's proforma invoice if one were to be generated. It will have similar line items and totals as a renewal preview, but the response will be presented in the format of a proforma invoice. Consequently it will include additional information such as the name and addresses that will appear on the proforma invoice.
    /// <para>
    /// The preview endpoint is subject to all the same conditions as the proforma invoice endpoint. For example, previews are only available on the Relationship Invoicing architecture, and previews cannot be made for end-of-life subscriptions.
    /// </para>
    /// <para>
    /// If all the data returned in the preview is as expected, you may then create a static proforma invoice and send it to your customer. The data within a preview will not be saved and will not be accessible after the call is made.
    /// </para>
    /// <para>
    /// Alternatively, if you have some proforma invoices already, you may make a preview call to determine whether any billing information for the subscription's upcoming renewal has changed.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> PreviewProformaInvoice(int subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/proforma_invoices/preview.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<ProformaInvoice>(),
            PreviewProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create signup proforma preview
    /// </summary>
    /// <param name="include">Choose to include a proforma invoice preview for the first renewal. Use in query <c>include=next_proforma_invoice</c>.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SignupProformaPreviewResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PreviewSignupProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a signup preview in the format of a proforma invoice to preview costs before a subscription's signup. This endpoint is only available for Relationship Invoicing sites and cannot be used to create consolidated proforma invoice previews or preview prepaid subscriptions. You have the option of previewing the first renewal's costs as well. The proforma invoice preview will not be persisted.
    /// <para>
    /// Pass a payload that resembles a subscription create or signup preview request. For example, you can specify components, coupons/a referral, offers, custom pricing, and an existing customer or payment profile to populate a shipping or billing address.
    /// </para>
    /// <para>
    /// A product and customer first name, last name, and email are the minimum requirements.
    /// </para>
    /// </remarks>
    public Task<SignupProformaPreviewResponse> PreviewSignupProformaInvoice(CreateSignupProformaPreviewInclude? include,
        CreateSubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/proforma_invoices/preview.json"),
            [],
            [new Param("include", include)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SignupProformaPreviewResponse>(),
            PreviewSignupProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Proforma Invoice
    /// </summary>
    /// <param name="proformaInvoiceUid">The uid of the proforma invoice</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns the details of an existing proforma invoice.
    /// <para>
    /// ## Restrictions
    /// </para>
    /// <para>
    /// Proforma invoices are only available on Relationship Invoicing sites.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> ReadProformaInvoice(string proformaInvoiceUid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/proforma_invoices/{proforma_invoice_uid}.json"),
            [new TemplateParam("proforma_invoice_uid", proformaInvoiceUid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProformaInvoice>(),
            ReadProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Void Proforma Invoice
    /// </summary>
    /// <param name="proformaInvoiceUid">The uid of the proforma invoice</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProformaInvoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="VoidProformaInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Voids a proforma invoice that has the status "draft".
    /// <para>
    /// ## Restrictions
    /// </para>
    /// <para>
    /// Proforma invoices are only available on Relationship Invoicing sites.
    /// </para>
    /// <para>
    /// Only proforma invoices that have the appropriate status may be reopened. If the invoice identified by {uid} does not have the appropriate status, the response will have HTTP status code 422 and an error message.
    /// </para>
    /// <para>
    /// A reason for the void operation is required to be included in the request body. If one is not provided, the response will have HTTP status code 422 and an error message.
    /// </para>
    /// </remarks>
    public Task<ProformaInvoice> VoidProformaInvoice(string proformaInvoiceUid,
        VoidInvoiceRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/proforma_invoices/{proforma_invoice_uid}/void.json"),
            [new TemplateParam("proforma_invoice_uid", proformaInvoiceUid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProformaInvoice>(),
            VoidProformaInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
