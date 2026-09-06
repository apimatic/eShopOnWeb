using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);
                var response = new ListUserSubscriptionsResponse();

                foreach (var subscription in subscriptions)
                {
                    response.Subscriptions.Add(new SubscriptionDetailsDto
                    {
                        Id = subscription.Id,
                        CustomerId = subscription.CustomerId,
                        PlanHandle = subscription.PlanHandle,
                        PlanName = subscription.PlanName,
                        Status = subscription.Status,
                        PriceInCents = subscription.PriceInCents,
                        PriceFormatted = subscription.PriceFormatted,
                        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                        NextBillingAt = subscription.NextBillingAt
                    });
                }

                return Results.Ok(response);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException();
    }
}

public class ListUserSubscriptionsResponse
{
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public List<SubscriptionDetailsDto> Subscriptions { get; set; } = new();
}
