using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopperSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateShopperSubscriptionRequest request,
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billing, userManager, user, cancellationToken);
            })
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing) =>
        throw new System.NotSupportedException("Use the routed handler.");

    private async Task<IResult> HandleAsync(
        CreateShopperSubscriptionRequest request,
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "productHandle is required." });
        }

        var shopper = await ShopperIdentityFactory.FromUserAsync(userManager, user);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(shopper, request.ProductHandle.Trim(), cancellationToken);
        var response = new CreateShopperSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(result.Subscription),
            Created = result.Created
        };

        if (result.Created)
        {
            return Results.Created($"api/subscriptions/{result.Subscription.Id}", response);
        }

        return Results.Ok(response);
    }

    internal static ShopperSubscriptionDto Map(ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };
}
