using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a Maxio plan. Idempotent: a double-click returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Results.Unauthorized();
        }

        var shopper = await ShopperResolver.FromHttpContextAsync(httpContext, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { error = "productHandle is required." });
        }

        var result = await billing.SubscribeAsync(shopper, request.ProductHandle);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(result.Subscription)
        };

        return result.Created
            ? Results.Created($"api/subscriptions/{result.Subscription.Id}", response)
            : Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Billing.CustomerSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
}
