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

/// <summary>
/// UC1 read side — the authenticated user's subscriptions as seen by the provider. JWT-secured.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new MySubscriptionsRequest { UserName = user.Identity?.Name };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserName))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(request.UserName);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Populated server-side from the bearer token.</summary>
    public string? UserName { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public MySubscriptionsResponse() { }

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
