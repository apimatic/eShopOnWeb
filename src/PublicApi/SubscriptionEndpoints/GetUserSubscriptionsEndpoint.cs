using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, GetUserSubscriptionsRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                return await HandleAsync(new GetUserSubscriptionsRequest(), httpContext, maxioService);
            })
            .RequireAuthorization()
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetUserSubscriptionsRequest request)
    {
        return Results.Ok(new { message = "OK" });
    }

    private async Task<IResult> HandleAsync(GetUserSubscriptionsRequest request, HttpContext httpContext, IMaxioService maxioService)
    {
        var response = new GetUserSubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Errors.Add("User not authenticated");
                return Results.Unauthorized();
            }

            var subscriptions = await maxioService.GetUserSubscriptionsAsync(userId);

            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionResponse
                {
                    Id = subscription.Id,
                    CustomerId = subscription.CustomerId,
                    State = subscription.State,
                    ProductHandle = subscription.ProductHandle,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    CreatedAt = subscription.CreatedAt,
                    UpdatedAt = subscription.UpdatedAt
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Errors.Add(ex.Message);
            return Results.BadRequest(response);
        }
    }
}

public class GetUserSubscriptionsRequest : BaseRequest
{
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionResponse> Subscriptions { get; } = new();
    public List<string> Errors { get; } = new();
}
