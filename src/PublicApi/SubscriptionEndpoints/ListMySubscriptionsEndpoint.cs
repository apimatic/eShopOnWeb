using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpRequest http, ISubscriptionService subscriptionService) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    UserId = http.HttpContext.User.FindFirstValue(ClaimTypes.Name)
                        ?? http.HttpContext.User.FindFirst("sub")?.Value
                        ?? throw new InvalidOperationException("User identity not found in token.")
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(request.UserId);

        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductPriceInCents = s.ProductPriceInCents,
                ProductPriceInDollars = s.ProductPriceInCents / 100.0,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt,
                Currency = s.Currency,
                ProductName = s.Product?.Name,
                ProductHandle = s.Product?.Handle,
                CustomerId = s.Customer?.Id,
                CustomerEmail = s.Customer?.Email
            }).ToList()
        };

        return Results.Ok(response);
    }
}
