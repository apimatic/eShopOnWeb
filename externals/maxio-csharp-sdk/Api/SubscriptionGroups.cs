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

public sealed class SubscriptionGroups
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionGroups(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add Subscription to Group
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Adds an existing subscription to a subscription group. For sites making use of the <see href="https://maxio.zendesk.com/hc/en-us/articles/24252287829645-Advanced-Billing-Invoices-Overview">Relationship Billing</see> and <see href="https://maxio.zendesk.com/hc/en-us/articles/24252185211533-Customer-Hierarchies-WhoPays#customer-hierarchies">Customer Hierarchy</see> features, it is possible to add existing subscriptions to subscription groups.
    /// <para>
    /// Passing <c>group</c> parameters with a <c>target</c> containing a <c>type</c> and optional <c>id</c> is all that's needed. When the <c>target</c> parameter specifies a <c>"customer"</c> or <c>"subscription"</c> that is already part of a hierarchy, the subscription will become a member of the customer's subscription group.  If the target customer or subscription is not part of a subscription group, a new group will be created and the subscription will become part of the group with the specified target customer set as the responsible payer for the group's subscriptions.
    /// </para>
    /// <para>
    /// <b>Note:</b> In order to add an existing subscription to a subscription group, it must belong to either the same customer record as the target, or be within the same customer hierarchy.
    /// </para>
    /// <para>
    /// Rather than specifying a customer, the <c>target</c> parameter could instead simply have a value of
    /// * <c>"self"</c> which indicates the subscription will be paid for not by some other customer, but by the subscribing customer,
    /// * <c>"parent"</c> which indicates the subscription will be paid for by the subscribing customer's parent within a customer hierarchy, or
    /// * <c>"eldest"</c> which indicates the subscription will be paid for by the root-level customer in the subscribing customer's hierarchy.
    /// </para>
    /// <para>
    /// To create a new subscription into a subscription group, reference the following:
    /// <see href="https://developers.chargify.com/docs/api-docs/d571659cf0f24-create-subscription#subscription-in-a-subscription-group">Create Subscription in a Subscription Group</see>
    /// </para>
    /// </remarks>
    public Task<SubscriptionGroupResponse> AddSubscriptionToGroup(int subscriptionId,
        AddSubscriptionToAGroup? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/group.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionGroupResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Subscription Group
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a subscription group with given members.
    /// </remarks>
    public Task<SubscriptionGroupResponse> CreateSubscriptionGroup(CreateSubscriptionGroupRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionGroupResponse>(),
            CreateSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Subscription Group
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="DeleteSubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a subscription group.
    ///  Only groups without members can be deleted.
    /// </remarks>
    public Task<DeleteSubscriptionGroupResponse> DeleteSubscriptionGroup(string uid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<DeleteSubscriptionGroupResponse>(),
            DeleteSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Find Subscription Group
    /// </summary>
    /// <param name="subscriptionId">The Advanced Billing id of the subscription associated with the subscription group</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FullSubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FindSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Finds the subscription group associated with a subscription.
    /// <para>
    /// If the subscription is not in a group, the endpoint will return a 404 code.
    /// </para>
    /// </remarks>
    public Task<FullSubscriptionGroupResponse> FindSubscriptionGroup(string subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/lookup.json"),
            [],
            [new Param("subscription_id", subscriptionId)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FullSubscriptionGroupResponse>(),
            FindSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Groups
    /// </summary>
    /// <param name="include">A list of additional information to include in the response. The following values are supported:  - <c>account_balances</c>: Account balance information for the subscription groups. Use in query: <c>include[]=account_balances</c></param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSubscriptionGroupsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists subscription groups for the site. The response is paginated and will return a <c>meta</c> key with pagination information.
    /// <para>
    /// #### Account Balance Information
    /// </para>
    /// <para>
    /// Account balance information for the subscription groups is not returned by default. If this information is desired, the <c>include[]=account_balances</c> parameter must be provided with the request.
    /// </para>
    /// </remarks>
    public Task<ListSubscriptionGroupsResponse> ListSubscriptionGroups(IReadOnlyList<SubscriptionGroupsListInclude>? include,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups.json"),
            [],
            [new Param("page", page), new Param("per_page", perPage), new Param("include", include)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSubscriptionGroupsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Subscription Group
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="include">Allows including additional data in the response. Use in query: <c>include[]=current_billing_amount_in_cents</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FullSubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns subscription group details.
    /// <para>
    /// #### Current Billing Amount in Cents
    /// </para>
    /// <para>
    /// Current billing amount for the subscription group is not returned by default. If this information is desired, the <c>include[]=current_billing_amount_in_cents</c> parameter must be provided with the request.
    /// </para>
    /// </remarks>
    public Task<FullSubscriptionGroupResponse> ReadSubscriptionGroup(string uid,
        IReadOnlyList<SubscriptionGroupInclude>? include,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}.json"),
            [new TemplateParam("uid", uid)],
            [new Param("include", include)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FullSubscriptionGroupResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Remove Subscription from Group
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RemoveSubscriptionFromGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Removes an existing subscription from a subscription group. For sites making use of the <see href="https://maxio.zendesk.com/hc/en-us/articles/24252287829645-Advanced-Billing-Invoices-Overview">Relationship Billing</see> and <see href="https://maxio.zendesk.com/hc/en-us/articles/24252185211533-Customer-Hierarchies-WhoPays#customer-hierarchies">Customer Hierarchy</see> features, it is possible to remove an existing subscription from a subscription group.
    /// </remarks>
    public Task RemoveSubscriptionFromGroup(int subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/group.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RemoveSubscriptionFromGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Subscription Group Signup
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionGroupSignupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SignupWithSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates multiple subscriptions at once under the same customer and consolidates them into a subscription group.
    /// <para>
    /// You must provide one and only one of the <c>payer_id</c>/<c>payer_reference</c>/<c>payer_attributes</c> for the customer attached to the group.
    /// </para>
    /// <para>
    /// You must provide one and only one of the <c>payment_profile_id</c>/<c>credit_card_attributes</c>/<c>bank_account_attributes</c> for the payment profile attached to the group.
    /// </para>
    /// <para>
    /// Only one of the <c>subscriptions</c> can have <c>"primary": true</c> attribute set.
    /// </para>
    /// <para>
    /// When passing a product to a subscription you can use either <c>product_id</c> or <c>product_handle</c> or <c>offer_id</c>. You can also use <c>custom_price</c> instead.
    /// The subscription request examples below will be split into two sections.
    /// The first section, "Subscription Customization", will focus on passing different information with a subscription, such as components, calendar billing, and custom fields. These examples will presume you are using a secure chargify_token generated by Maxio.js (formerly Chargify.js).
    /// </para>
    /// </remarks>
    public Task<SubscriptionGroupSignupResponse> SignupWithSubscriptionGroup(SubscriptionGroupSignupRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/signup.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionGroupSignupResponse>(),
            SignupWithSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Subscription Group Members
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateSubscriptionGroupMembersError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates subscription group members.
    /// <c>"member_ids"</c> should contain an array of both subscription IDs to set as group members and subscription IDs already present in the groups. Not including them will result in removing them from the subscription group. To clean up members, just leave the array empty.
    /// </remarks>
    public Task<SubscriptionGroupResponse> UpdateSubscriptionGroupMembers(string uid,
        UpdateSubscriptionGroupRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionGroupResponse>(),
            UpdateSubscriptionGroupMembersErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
