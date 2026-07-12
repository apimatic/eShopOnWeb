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

/// <summary>Lists the authenticated caller's own subscriptions.</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), new SubscriptionEndpointContext(subscriptionService, user));
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, SubscriptionEndpointContext context)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        var userReference = SubscriptionEndpointHelpers.RequireUserReference(context.User);
        var subscriptions = await context.SubscriptionService.GetMySubscriptionsAsync(userReference);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMapper.ToDto));
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
