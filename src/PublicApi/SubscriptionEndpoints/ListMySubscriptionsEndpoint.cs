using System;
using System.Collections.Generic;
using System.Linq;
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
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated shopper subscriptions (state, price, next billing date)
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, SubscriptionCaller>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService service, SubscriptionCaller caller, CancellationToken ct) =>
            {
                return await HandleAsync(service, caller, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service, SubscriptionCaller caller)
        => HandleAsync(service, caller, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService service, SubscriptionCaller caller, CancellationToken ct)
    {
        var identity = await caller.ResolveAsync();
        if (identity is null)
            return Results.Unauthorized();

        var items = await service.ListMySubscriptionsAsync(identity.UserId, ct);
        return Results.Ok(new ListMySubscriptionsResponse
        {
            Subscriptions = items.Select(SubscriptionDto.From).ToList(),
        });
    }
}
