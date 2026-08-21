using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing)
    {
        var identity = BillingIdentityFactory.Create(user);
        return Results.Ok(await billing.ListSubscriptionsAsync(identity, default));
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user,
                    ISubscriptionBillingService billing,
                    CancellationToken cancellationToken) =>
                {
                    var identity = BillingIdentityFactory.Create(user);
                    return Results.Ok(await billing.ListSubscriptionsAsync(identity, cancellationToken));
                })
            .Produces<IReadOnlyList<SubscriptionDetails>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
