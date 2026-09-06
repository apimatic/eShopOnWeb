using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, GetUserSubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new GetUserSubscriptionsRequest(userId), subscriptionService);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(GetUserSubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(request.UserId);

            var response = new GetUserSubscriptionsResponse()
            {
                Subscriptions = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    Balance = s.BalanceInCents / 100m,
                    NextBillingDate = s.NextBillingDate,
                    CreatedAt = s.CreatedAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class GetUserSubscriptionsRequest : BaseRequest
{
    public GetUserSubscriptionsRequest(string userId)
    {
        UserId = userId;
    }

    public string UserId { get; set; }
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
