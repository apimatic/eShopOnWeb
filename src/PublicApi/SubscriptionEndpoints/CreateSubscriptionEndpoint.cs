using System.Globalization;
using System.Security.Claims;
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
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the shopper
/// (idempotent on the eShopOnWeb user id) and enrolls them; a double-click returns the existing
/// subscription rather than creating a second one.
/// POST /api/subscriptions
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
                await HandleAsync(request, user))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var subscriber = await SubscriptionMapping.ResolveSubscriberAsync(user, _userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(subscriber, request.PlanHandle.Trim());

        var dto = result.Subscription.ToDto();
        response.Subscription = dto;
        response.AlreadyExisted = result.AlreadyExisted;
        response.Message = BuildMessage(result.AlreadyExisted, dto);

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }

    private static string BuildMessage(bool alreadyExisted, CustomerSubscriptionDto dto)
    {
        var verb = alreadyExisted ? "You are already subscribed to" : "You are now subscribed to";
        var price = dto.Price.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        var nextBilling = dto.NextBillingAt.HasValue
            ? $" Next billing on {dto.NextBillingAt.Value:yyyy-MM-dd}."
            : string.Empty;
        return $"{verb} {dto.PlanName ?? dto.PlanHandle} ({price}). Subscription is {dto.State}.{nextBilling}";
    }
}
