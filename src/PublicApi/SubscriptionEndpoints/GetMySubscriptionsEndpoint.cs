using System;
using System.Collections.Generic;
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

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptionService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);

            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductName = subscription.ProductName,
                    ProductHandle = subscription.ProductHandle,
                    PricePerMonth = subscription.PricePerMonth,
                    CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextBillingDate = subscription.NextAssessmentAt
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
