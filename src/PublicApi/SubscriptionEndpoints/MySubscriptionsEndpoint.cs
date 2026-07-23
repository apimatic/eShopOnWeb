using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the authenticated user's own subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
                await HandleAsync(new MySubscriptionsRequest(user, cancellationToken), subscriptionService))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var userName = SubscriptionActorResolver.ResolveUserName(request.User);
        if (userName is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(userName, request.CancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromSubscription));

        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        User = user;
        CancellationToken = cancellationToken;
    }

    internal ClaimsPrincipal User { get; }

    internal CancellationToken CancellationToken { get; }
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
