using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Idempotent: the billing customer is created only if one does not already exist, and a shopper
/// who is already subscribed to the plan gets that enrollment back with <c>created: false</c> and
/// 200 OK rather than a second subscription. A double-clicked button therefore bills once.
/// </remarks>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // Scoped services are taken as handler parameters, never constructor-injected: endpoint
        // instances are resolved once at startup, so a constructor-injected DbContext-backed service
        // would be shared by every request and fail under concurrency.
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, ClaimsPrincipal user, ISubscriptionBillingService billingService,
             SubscriberIdentityAccessor subscriberIdentityAccessor, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    request ?? new SubscribeRequest(),
                    user,
                    billingService,
                    subscriberIdentityAccessor,
                    cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>
    /// Overload required by <see cref="IEndpoint{TResponse, TRequest, TService}"/>. It has no
    /// principal and no identity accessor, so it can only answer 401; the route above supplies both.
    /// </summary>
    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        SubscriberIdentityAccessor subscriberIdentityAccessor,
        CancellationToken cancellationToken)
    {
        var subscriber = await subscriberIdentityAccessor.ResolveAsync(user, request.FirstName, request.LastName);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created
        };

        // 201 for a new enrollment, 200 when an existing one was returned, so a client can tell a
        // first click from a repeat without inspecting the body.
        return result.Created
            ? Results.Created(
                $"api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}",
                response)
            : Results.Ok(response);
    }
}
