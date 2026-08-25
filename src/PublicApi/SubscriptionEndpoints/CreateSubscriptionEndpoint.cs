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
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: re-posting the same plan
/// returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal,
                UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, claimsPrincipal, userManager, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal,
        UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var buyer = await BuyerResolver.ResolveAsync(claimsPrincipal, userManager);
        if (buyer == null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var subscription = await billingService.SubscribeAsync(
            buyer.Value.BuyerId, buyer.Value.Email, request.ProductHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };
        return Results.Ok(response);
    }

    internal static SubscriptionDto Map(SubscriptionDetails details) => new SubscriptionDto
    {
        SubscriptionId = details.SubscriptionId,
        State = details.State,
        PlanName = details.PlanName,
        PlanHandle = details.PlanHandle,
        PriceInCents = details.PriceInCents,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        ActivatedAt = details.ActivatedAt,
        NextBillingAt = details.NextBillingAt,
        BalanceInCents = details.BalanceInCents
    };
}
