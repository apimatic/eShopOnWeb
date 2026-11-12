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

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated user's subscriptions (state and next billing date are read live from Maxio)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService service, CancellationToken ct) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();
                return await HandleAsync(service, userName, ct);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service) =>
        throw new NotSupportedException("Identity is required; use the overload taking a user name.");

    public async Task<IResult> HandleAsync(ISubscriptionService service, string userName, CancellationToken ct)
    {
        var subs = await service.GetMySubscriptionsAsync(userName, ct);
        return Results.Ok(new MySubscriptionsResponse(Guid.NewGuid())
        {
            Subscriptions = subs.Select(SubscriptionDto.From).ToList()
        });
    }
}
