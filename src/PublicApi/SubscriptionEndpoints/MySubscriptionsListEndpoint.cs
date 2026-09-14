using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's subscriptions, read live from Maxio (the billing system of record).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? user.FindFirst(ClaimTypes.Name)?.Value
                        ?? string.Empty
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(request.UserId);
        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
