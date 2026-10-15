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

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// List the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, string, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user.Identity?.Name ?? string.Empty, subscriptions, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(string userName, ISubscriptionService subscriptions) =>
        HandleAsync(userName, subscriptions, CancellationToken.None);

    public async Task<IResult> HandleAsync(string userName, ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        var mine = await subscriptions.ListMineAsync(userName, cancellationToken);
        return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = mine.Select(SubscriptionDto.From).ToList() });
    }
}
