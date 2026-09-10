using System.Security.Claims;
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
/// Subscribes the authenticated shopper to a plan. Ensures a billing customer exists for the user
/// and enrolls them idempotently: a repeated (or double-clicked) request returns the existing live
/// subscription rather than creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                request.Subscriber = SubscriberIdentityFactory.FromPrincipal(user);
                return await HandleAsync(request, subscriptionBillingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService subscriptionBillingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A 'planHandle' is required to subscribe.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionBillingService.SubscribeAsync(request.Subscriber!, request.PlanHandle);
        response.Subscription = CustomerSubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
