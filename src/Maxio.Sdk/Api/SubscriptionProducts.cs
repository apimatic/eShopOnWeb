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

public sealed class SubscriptionProducts
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionProducts(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Migrate Subscription Product
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="MigrateSubscriptionProductError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Migrates a subscription to a different product.
    /// <para>
    /// In order to create a migration, you must pass the <c>product_id</c> or <c>product_handle</c> in the object when you send a POST request. You may also pass either a <c>product_price_point_id</c> or <c>product_price_point_handle</c> to choose which price point the subscription is moved to. If no price point identifier is passed the subscription will be moved to the products default price point. The response will be the updated subscription.
    /// </para>
    /// <para>
    /// ## Valid Subscriptions
    /// </para>
    /// <para>
    /// Subscriptions should be in the <c>active</c> or <c>trialing</c> state in order to be migrated.
    /// </para>
    /// <para>
    /// (For backwards compatibility reasons, it is possible to migrate a subscription that is in the <c>trial_ended</c> state via the API, however this is not recommended.  Since <c>trial_ended</c> is an end-of-life state, the subscription should be canceled, the product changed, and then the subscription can be reactivated.)
    /// </para>
    /// <para>
    /// ## Migrations Documentation
    /// </para>
    /// <para>
    /// Full documentation on how to record Migrations in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24181589372429-Data-Migration-to-Advanced-Billing">here</see>.
    /// </para>
    /// <para>
    /// ## Failed Migrations
    /// </para>
    /// <para>
    /// Important note: One of the most common ways that a migration can fail is when the attempt is made to migrate a subscription to its current product.
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
    public Task<SubscriptionResponse> MigrateSubscriptionProduct(int subscriptionId,
        SubscriptionProductMigrationRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/migrations.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionResponse>(),
            MigrateSubscriptionProductErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Preview Subscription Product Migration
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionMigrationPreviewResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PreviewSubscriptionProductMigrationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Previews the charges resulting from migrating a subscription to a different product.
    /// <para>
    /// ## Previewing a future date
    /// It is also possible to preview the migration for a date in the future, as long as it's still within the subscription's current billing period, by passing a <c>proration_date</c> along with the request (e.g., <c>"proration_date": "2020-12-18T18:25:43.511Z"</c>).
    /// </para>
    /// <para>
    /// This will calculate the prorated adjustment, charge, payment and credit applied values assuming the migration is done at that date in the future as opposed to right now.
    /// </para>
    /// </remarks>
    public Task<SubscriptionMigrationPreviewResponse> PreviewSubscriptionProductMigration(int subscriptionId,
        SubscriptionMigrationPreviewRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/migrations/preview.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionMigrationPreviewResponse>(),
            PreviewSubscriptionProductMigrationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
