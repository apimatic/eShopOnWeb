using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// The call is idempotent: repeating it (a double-clicked button, a client retry after a timeout)
/// returns the subscription that already exists rather than creating a second one, and answers 200
/// instead of 201 so the caller can tell the two apart.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequestBody? body,
             ClaimsPrincipal caller,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberIdentityResolver.ResolveAsync(caller, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                var request = new CreateSubscriptionRequest(subscriber, body ?? new SubscribeRequestBody(), cancellationToken);
                return await HandleAsync(request, billingService);
            })
            .Accepts<SubscribeRequestBody>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(
            new SubscribeRequest(request.Subscriber, request.Body.PlanHandle, request.Body.IdempotencyKey),
            request.CancellationToken);

        response.Created = result.Created;
        response.Subscription = result.Subscription.ToDto();

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
