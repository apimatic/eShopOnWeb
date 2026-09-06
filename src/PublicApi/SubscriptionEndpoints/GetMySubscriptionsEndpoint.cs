using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await maxioService.GetUserSubscriptionsAsync(userId);

                var response = new GetMySubscriptionsResponse();
                response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    MaxioSubscriptionId = s.MaxioSubscriptionId,
                    PlanHandle = s.PlanHandle,
                    State = s.State,
                    NextBillingAt = s.NextBillingAt,
                    PriceInCents = s.PriceInCents
                }).ToList();

                return Results.Ok(response);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions")
            ;
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException("This method is not called directly");
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
