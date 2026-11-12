using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Staff: sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, CancellationToken ct, BillingService billing) =>
            {
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}
