using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<IReadOnlyList<SubscriptionDto>>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(SubscriptionBillingService service)
    {
        var context = _httpContextAccessor.HttpContext;
        var user = CurrentUserIdentity.From(context?.User ?? new System.Security.Claims.ClaimsPrincipal());
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.GetMySubscriptionsAsync(
            user,
            context?.RequestAborted ?? CancellationToken.None));
    }
}
