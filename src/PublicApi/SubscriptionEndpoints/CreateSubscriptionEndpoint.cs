using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var username = UsernameOf(user);
                if (username is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(request, billingService, username);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, string.Empty);

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService, string username)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(username, request.ProductHandle);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            State = subscription.State,
            Price = subscription.Price,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextBillingDate
        };

        return Results.Ok(response);
    }

    internal static string? UsernameOf(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("unique_name")?.Value
        ?? user.Identity?.Name;
}
