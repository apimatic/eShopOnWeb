using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. The caller's identity is taken from their JWT,
/// a Maxio customer is ensured for them, and the enrollment is idempotent — a repeated or
/// concurrent request returns the existing subscription instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscribeEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var userName = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(userName, request.PlanHandle);

        response.Subscription = SubscriptionDto.FromDomain(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;
        response.CustomerId = result.CustomerId;
        response.Message = BuildMessage(result);

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }

    private static string BuildMessage(SubscribeResult result)
    {
        var subscription = result.Subscription;
        var plan = subscription.ProductName ?? subscription.ProductHandle ?? "the selected plan";
        var next = subscription.NextBillingAt is { } date
            ? $" Next billing date: {date:yyyy-MM-dd}."
            : string.Empty;

        return result.AlreadySubscribed
            ? $"You are already subscribed to {plan} (status: {subscription.State}).{next}"
            : $"Subscribed to {plan} (status: {subscription.State}).{next}";
    }
}
