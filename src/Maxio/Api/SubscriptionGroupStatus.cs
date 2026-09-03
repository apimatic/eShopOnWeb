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

public sealed class SubscriptionGroupStatus
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionGroupStatus(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Cancel Delayed Group Cancellation
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelDelayedCancellationForGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Removes the delayed cancellation on a subscription group.
    /// <para>
    /// Removing the delayed cancellation on a subscription group will ensure that the subscriptions do not get canceled at the end of the period. The request will reset the <c>cancel_at_end_of_period</c> flag to false on each member in the group.
    /// </para>
    /// </remarks>
    public Task CancelDelayedCancellationForGroup(string uid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/delayed_cancel.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            CancelDelayedCancellationForGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Cancel Grouped Subscriptions
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelSubscriptionsInGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancels all subscriptions within the specified group immediately. The group is identified by the <c>uid</c> that is passed in the URL. To successfully cancel the group, the primary subscription must be on automatic billing. The group members must be on automatic billing or prepaid.
    /// <para>
    /// To cancel a subscription group while also charging for any unbilled usage on metered or prepaid components, the <c>charge_unbilled_usage=true</c> parameter must be included in the request.
    /// </para>
    /// </remarks>
    public Task CancelSubscriptionsInGroup(string uid,
        CancelGroupedSubscriptionsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/cancel.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            CancelSubscriptionsInGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Initiate Delayed Group Cancellation
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="InitiateDelayedCancellationForGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Schedules all subscriptions within the specified group to be canceled at the end of their billing period. The group is identified by its uid passed in the URL.
    /// <para>
    /// All subscriptions in the group must be on automatic billing in order to successfully cancel them, and the group must not be in a "past_due" state.
    /// </para>
    /// </remarks>
    public Task InitiateDelayedCancellationForGroup(string uid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/delayed_cancel.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            InitiateDelayedCancellationForGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Reactivate / Resume Subscription Group
    /// </summary>
    /// <param name="uid">The uid of the subscription group</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReactivateSubscriptionGroupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReactivateSubscriptionGroupError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Reactivates or resumes a cancelled subscription group. Upon reactivation, any canceled invoices created after the beginning of the primary subscription's billing period will be reopened and payment will be attempted on them. If the subscription group is being reactivated (as opposed to resumed), new charges will also be assessed for the new billing period.
    /// <para>
    /// Whether a subscription group is reactivated (a new billing period is created) or resumed (the current billing period is respected) will depend on the parameters that are sent with the request as well as the date of the request relative to the primary subscription's period.
    /// </para>
    /// <para>
    /// ## Reactivating within the current period
    /// </para>
    /// <para>
    /// If a subscription group is cancelled and reactivated within the primary subscription's current period, we can choose to either start a new billing period or maintain the existing one. If we want to maintain the existing billing period, the <c>resume=true</c> option must be passed in request parameters.
    /// </para>
    /// <para>
    /// An exception to the above are subscriptions that are on calendar billing. These subscriptions cannot be reactivated within the current period. If the <c>resume=true</c> option is not passed, the request will return an error.
    /// </para>
    /// <para>
    /// The <c>resume_members</c> option is ignored in this case. All eligible group members will be automatically resumed.
    /// </para>
    /// <para>
    ///
    /// ## Reactivating beyond the current period
    /// </para>
    /// <para>
    /// In this case, a subscription group can only be reactivated with a new billing period. If the <c>resume=true</c> option is passed it will be ignored.
    /// </para>
    /// <para>
    /// Member subscriptions can have billing periods that are longer than the primary (e.g. a monthly primary with annual group members). If the primary subscription in a group cannot be reactivated within the current period, but other group members can be, passing <c>resume_members=true</c> will resume the existing billing period for eligible group members. The primary subscription will begin a new billing period.
    /// </para>
    /// <para>
    /// For calendar billing subscriptions, the new billing period created will be a partial one, spanning from the date of reactivation to the next corresponding calendar renewal date.
    /// </para>
    /// <para>
    /// ## 3D Secure (3DS) Authentication post-authentication flow
    /// </para>
    /// <para>
    /// When a payment requires 3DS Authentication to adhere to Strong Customer Authentication (SCA), the request enters a post-authentication flow where a 422 Unprocessable Entity status is returned with an action_link that will direct the customer through 3DS Authentication.
    /// </para>
    /// <para>
    /// See the <see href="https://docs.maxio.com/hc/en-us/articles/44277749524365-3D-Secure-Post-Authentication-Flow">3D Secure Post-Authentication Flow</see> article in the product documentation to learn how to manage the redirect flow.
    /// </para>
    /// </remarks>
    public Task<ReactivateSubscriptionGroupResponse> ReactivateSubscriptionGroup(string uid,
        ReactivateSubscriptionGroupRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscription_groups/{uid}/reactivate.json"),
            [new TemplateParam("uid", uid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ReactivateSubscriptionGroupResponse>(),
            ReactivateSubscriptionGroupErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
