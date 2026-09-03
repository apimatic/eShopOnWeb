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

public sealed class BillingPortal
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal BillingPortal(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Enable Billing Portal for Customer
    /// </summary>
    /// <param name="customerId">The Chargify id of the customer</param>
    /// <param name="autoInvite">When set to 1, an Invitation email will be sent to the Customer. When set to 0, or not sent, an email will not be sent. Use in query: <c>auto_invite=1</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="EnableBillingPortalForCustomerError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Enables Billing Portal access for a customer, with an option to send an invitation email at the same time.
    /// <para>
    /// ## Billing Portal Documentation
    /// </para>
    /// <para>
    /// Full documentation on how the Billing Portal operates within the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24252412965133-Billing-Portal-Overview">here</see>.
    /// </para>
    /// <para>
    /// This documentation is focused on how to configure the Billing Portal Settings, as well as Subscriber Interaction and Merchant Management of the Billing Portal.
    /// </para>
    /// <para>
    /// You can use this endpoint to enable Billing Portal access for a Customer, with the option of sending the Customer an Invitation email at the same time.
    /// </para>
    /// <para>
    /// ## Billing Portal Security
    /// </para>
    /// <para>
    /// If your customer has been invited to the Billing Portal, then they will receive a link to manage their subscription (the “Management URL”) automatically at the bottom of their statements, invoices, and receipts. <b>This link changes periodically for security and is only valid for 65 days.</b>
    /// </para>
    /// <para>
    /// If you need to provide your customer their Management URL through other means, you can retrieve it via the API. Because the URL is cryptographically signed with a timestamp, it is not possible for merchants to generate the URL without requesting it from Advanced Billing.
    /// </para>
    /// <para>
    /// In order to prevent abuse &amp; overuse, we ask that you request a new URL only when absolutely necessary. Management URLs are good for 65 days, so you should re-use a previously generated one as much as possible. If you use the URL frequently (such as to display on your website), <b>do not</b> make an API request to Advanced Billing every time.
    /// </para>
    /// </remarks>
    public Task<CustomerResponse> EnableBillingPortalForCustomer(int customerId,
        AutoInvite? autoInvite,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/portal/customers/{customer_id}/enable.json"),
            [new TemplateParam("customer_id", customerId)],
            [new Param("auto_invite", autoInvite)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<CustomerResponse>(),
            EnableBillingPortalForCustomerErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Billing Portal Management Link
    /// </summary>
    /// <param name="customerId">The Chargify id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PortalManagementLink"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadBillingPortalLinkError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns the exact URL required for a subscriber to access the Billing Portal.
    /// <para>
    /// ## Rules for Management Link API
    /// </para>
    /// <list type="bullet">
    ///   <item><description>When retrieving a management URL, multiple requests for the same customer in a short period will return the <b>same</b> URL</description></item>
    ///   <item><description>We will not generate a new URL for 15 days</description></item>
    ///   <item><description>You must cache and remember this URL if you are going to need it again within 15 days</description></item>
    ///   <item><description>Only request a new URL after the <c>new_link_available_at</c> date</description></item>
    ///   <item><description>You are limited to 15 requests for the same URL. If you make more than 15 requests before <c>new_link_available_at</c>, you will be blocked from further Management URL requests (with a response code <c>429</c>).</description></item>
    /// </list>
    /// </remarks>
    public Task<PortalManagementLink> ReadBillingPortalLink(int customerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/portal/customers/{customer_id}/management_link.json"),
            [new TemplateParam("customer_id", customerId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PortalManagementLink>(),
            ReadBillingPortalLinkErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Resend Billing Portal Invitation
    /// </summary>
    /// <param name="customerId">The Chargify id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ResentInvitation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ResendBillingPortalInvitationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Resends a customer's Billing Portal invitation.
    /// <para>
    /// If you attempt to resend an invitation 5 times within 30 minutes, you will receive a <c>422</c> response with an <c>error</c> message in the body.
    /// </para>
    /// <para>
    /// If you attempt to resend an invitation when the Billing Portal is already disabled for a Customer, you will receive a <c>422</c> error response.
    /// </para>
    /// <para>
    /// If you attempt to resend an invitation when the Customer does not exist, you will receive a <c>404</c> error response.
    /// </para>
    /// <para>
    /// ## Limitations
    /// </para>
    /// <para>
    /// This endpoint will only return a JSON response.
    /// </para>
    /// </remarks>
    public Task<ResentInvitation> ResendBillingPortalInvitation(int customerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/portal/customers/{customer_id}/invitations/invite.json"),
            [new TemplateParam("customer_id", customerId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<ResentInvitation>(),
            ResendBillingPortalInvitationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Revoke Billing Portal Invitation for Customer
    /// </summary>
    /// <param name="customerId">The Chargify id of the customer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="RevokedInvitation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Revokes a customer's Billing Portal invitation.
    /// <para>
    /// If you attempt to revoke an invitation when the Billing Portal is already disabled for a Customer, you will receive a 422 error response.
    /// </para>
    /// <para>
    /// ## Limitations
    /// </para>
    /// <para>
    /// This endpoint will only return a JSON response.
    /// </para>
    /// </remarks>
    public Task<RevokedInvitation> RevokeBillingPortalAccess(int customerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/portal/customers/{customer_id}/invitations/revoke.json"),
            [new TemplateParam("customer_id", customerId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<RevokedInvitation>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
