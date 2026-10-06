using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var identity = await ResolveUserAsync();
        if (identity is null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        var result = await subscriptionService.SubscribeAsync(identity.Value.UserId, identity.Value.Email, request.PlanHandle, GetRequestAborted());

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(result.Subscription)
        };
        if (result.Created)
        {
            return Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response);
        }
        return Results.Ok(response);
    }

    private System.Threading.CancellationToken GetRequestAborted() =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

    private async Task<(string UserId, string Email)?> ResolveUserAsync()
    {
        var email = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return null;
        return (user.Id, user.Email ?? email);
    }

    private static SubscriptionDto ToDto(MaxioSubscriptionInfo info) => new()
    {
        Id = info.Id,
        PlanHandle = info.PlanHandle,
        PlanName = info.PlanName,
        PriceInCents = info.PriceInCents,
        Currency = info.Currency,
        State = info.State,
        NextBillingDate = info.NextBillingDate,
        CurrentPeriodEndsAt = info.CurrentPeriodEndsAt,
        CollectionMethod = info.CollectionMethod
    };
}