using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// Idempotent: repeating the same plan for the same user returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, httpContext, userManager, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        throw new System.NotSupportedException("Subscriptions are handled through the routed overload.");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billing)
    {
        var shopper = await ShopperResolver.FromHttpContextAsync(httpContext, userManager);
        var result = await billing.SubscribeAsync(shopper, request.ProductHandle);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(result.Subscription)
        };

        if (result.Created)
        {
            return Results.Created($"api/subscriptions/{result.Subscription.Id}", response);
        }

        return Results.Ok(response);
    }

    private static ShopperSubscriptionDto Map(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };
}
