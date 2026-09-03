using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopperSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billing, userManager, cancellationToken);
            })
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(
        CreateShopperSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var customerReference = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "productHandle is required." });
        }

        var email = await ResolveEmail(userManager, customerReference);
        var subscription = await billing.SubscribeAsync(customerReference, email, request.ProductHandle, cancellationToken);
        var response = new CreateShopperSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };

        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }

    internal static ShopperSubscriptionDto Map(ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt
        };

    internal static async Task<string> ResolveEmail(UserManager<ApplicationUser> userManager, string customerReference)
    {
        var applicationUser = await userManager.FindByNameAsync(customerReference);
        if (!string.IsNullOrWhiteSpace(applicationUser?.Email))
        {
            return applicationUser.Email;
        }

        return customerReference;
    }
}
