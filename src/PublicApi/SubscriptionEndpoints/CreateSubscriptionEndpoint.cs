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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// Idempotent: a double-submit returns the existing customer/subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, httpContext: null);

    private async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        HttpContext? httpContext)
    {
        if (httpContext?.User is null)
        {
            return Results.Unauthorized();
        }

        var user = await ShopperIdentity.GetRequiredUserAsync(httpContext, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(new SubscribeCommand
        {
            UserId = user.Id,
            Email = ShopperIdentity.Email(user),
            FirstName = ShopperIdentity.FirstName(user),
            LastName = ShopperIdentity.LastName(user),
            ProductHandle = request.ProductHandle?.Trim() ?? string.Empty
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = MapSubscription(result.Subscription),
            Created = result.Created
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }

    public static SubscriptionDto MapSubscription(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };
}
