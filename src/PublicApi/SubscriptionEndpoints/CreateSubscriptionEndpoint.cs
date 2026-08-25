using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the call for a
/// plan the shopper is already actively subscribed to returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
           .RequireAuthorization()
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        var shopper = await ShopperContext.ResolveAsync(user, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await _billingService.SubscribeAsync(shopper, request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            AlreadyExisted = result.AlreadyExisted,
            Subscription = Map(result.Subscription)
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }

    internal static SubscriptionDto Map(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.NextBillingAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };
}
