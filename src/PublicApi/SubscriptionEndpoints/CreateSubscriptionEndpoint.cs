using System.Security.Claims;
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

/// <summary>
/// Creates a Maxio subscription for the authenticated shopper (idempotent).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing, user, users);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, user: null, users: null);

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal? user,
        UserManager<ApplicationUser>? users)
    {
        var shopper = await ShopperIdentityResolver.FromAsync(user, users);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(shopper, request.ProductHandle);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ListMySubscriptionsResponse.FromSubscription(result.Subscription),
            AlreadyExisted = result.AlreadyExisted
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
