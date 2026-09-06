using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), httpContext, subscriptionService);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetUserSubscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException("This method is not used; use the other overload.");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService)
    {
        var userEmail = httpContext.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userEmail);

        var response = new GetUserSubscriptionsResponse(request.CorrelationId());
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionResponse
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                ProductPrice = subscription.ProductPrice,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
            });
        }

        return Results.Ok(response);
    }
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionResponse> Subscriptions { get; } = new();
}
