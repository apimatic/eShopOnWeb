using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Staff: sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, NextBillingDateRequest request, BillingService billing, CancellationToken ct) =>
                await HandleAsync(subscriptionId, request, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, NextBillingDateRequest request, BillingService billing, CancellationToken ct)
    {
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request?.Date, ct));
    }
}

public class NextBillingDateRequest
{
    /// <summary>The date to bill on, formatted YYYY-MM-DD.</summary>
    public DateOnly? Date { get; set; }
}
