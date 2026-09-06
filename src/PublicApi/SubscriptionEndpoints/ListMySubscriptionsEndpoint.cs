using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioBillingService billingService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                            httpContext.User.FindFirstValue("sub");

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                var subscriptions = await billingService.ListUserSubscriptionsAsync(userId);
                var response = new ListMySubscriptionsResponse(Guid.NewGuid())
                {
                    Subscriptions = new List<SubscriptionDto>(subscriptions)
                };

                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioBillingService billingService)
    {
        throw new NotImplementedException("This endpoint uses MapGet directly");
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = [];
    }

    public List<SubscriptionDto> Subscriptions { get; set; }
}
