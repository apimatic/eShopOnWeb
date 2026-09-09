using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated buyer to a plan. Idempotent: repeating the same
/// subscription request returns the existing subscription instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "PlanHandle is required." });
        }

        var buyer = await GetBuyerAsync();
        if (buyer is null)
        {
            return Results.Unauthorized();
        }

        SubscribeResult result;
        try
        {
            result = await subscriptionService.SubscribeAsync(buyer, request.PlanHandle.Trim());
        }
        catch (PlanNotFoundException)
        {
            return Results.NotFound(new { message = $"A subscription plan with handle '{request.PlanHandle}' was not found." });
        }
        catch (BillingException ex)
        {
            return Results.Json(new { message = $"The billing system rejected the subscription: {ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
        }

        response.Subscription = ToDto(result.Subscription);

        return result.CreatedNew
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private async Task<BuyerIdentity?> GetBuyerAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user is null ? null : BuyerIdentityFactory.Create(user);
    }

    internal static SubscriptionDto ToDto(SubscriptionView view) =>
        new()
        {
            Id = view.BillingSubscriptionId,
            BillingCustomerId = view.BillingCustomerId,
            PlanHandle = view.PlanHandle,
            PlanName = view.PlanName,
            PriceInCents = view.PriceInCents,
            Price = view.PriceInCents / 100m,
            State = view.State,
            CurrentPeriodStartsAt = view.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = view.CurrentPeriodEndsAt,
            NextBillingDate = view.NextBillingAt,
            CreatedAt = view.CreatedAt
        };
}
