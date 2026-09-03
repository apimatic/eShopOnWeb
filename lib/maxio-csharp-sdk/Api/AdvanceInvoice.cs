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

public sealed class AdvanceInvoice
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal AdvanceInvoice(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Issue advance invoice
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Invoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="IssueAdvanceInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Issues an invoice in advance for a subscription's next renewal date. <see href="https://maxio.zendesk.com/hc/en-us/articles/24252026404749-Issue-Invoice-In-Advance">See our docs</see> for more information on advance invoices, including eligibility for generating one; for the most part, they function like any other invoice, except they are issued early and have special behavior upon being voided.
    /// A subscription may only have one advance invoice per billing period. Attempting to issue an advance invoice when one already exists will return an error.
    /// That said, regeneration of the invoice may be forced with the params <c>force: true</c>, which will void an advance invoice if one exists and generate a new one. If no advance invoice exists, a new one will be generated.
    /// We recommend using either the create or preview endpoints for proforma invoices to preview this advance invoice before using this endpoint to generate it.
    /// </remarks>
    public Task<Invoice> IssueAdvanceInvoice(int subscriptionId,
        IssueAdvanceInvoiceRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/advance_invoice/issue.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Invoice>(),
            IssueAdvanceInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read advance invoice
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Invoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadAdvanceInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns the advance invoice generated for a subscription's upcoming renewal. There can only be one advance invoice per subscription per billing cycle.
    /// </remarks>
    public Task<Invoice> ReadAdvanceInvoice(int subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/advance_invoice.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Invoice>(),
            ReadAdvanceInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Void advance invoice
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Invoice"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="VoidAdvanceInvoiceError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Voids a subscription's existing advance invoice. Once voided, it can later be regenerated if desired.
    /// A <c>reason</c> is required in order to void, and the invoice must have an open status. Voiding will cause any prepayments and credits that were applied to the invoice to be returned to the subscription. For a full overview of the impact of voiding, <see href="$m/Invoice">see our help docs</see>.
    /// </remarks>
    public Task<Invoice> VoidAdvanceInvoice(int subscriptionId,
        VoidInvoiceRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/advance_invoice/void.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Invoice>(),
            VoidAdvanceInvoiceErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
