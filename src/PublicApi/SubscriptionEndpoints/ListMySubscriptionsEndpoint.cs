using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, UserManager<ApplicationUser> users, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                var user = await CallerIdentity.ResolveAsync(http, users);
                if (user is null)
                    return CallerIdentity.Fail(StatusCodes.Status401Unauthorized, "Unknown user.");

                var views = await billing.ListMySubscriptionsAsync(user, ct);
                return Results.Ok(new ListMySubscriptionsResponse(Guid.NewGuid())
                {
                    Subscriptions = views.Select(SubscriptionDto.From).ToList()
                });
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
