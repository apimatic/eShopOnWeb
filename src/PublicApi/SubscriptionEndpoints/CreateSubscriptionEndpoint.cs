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
/// Idempotent: a repeated subscribe for the same plan returns the existing enrollment.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest? request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var user = await ResolveUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(new SubscribeToPlanRequest
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            UserName = user.UserName ?? user.Email ?? string.Empty,
            ProductHandle = request.ProductHandle
        });

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

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return null;
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user is not null)
        {
            return user;
        }

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }

    internal static ShopperSubscriptionDto Map(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
