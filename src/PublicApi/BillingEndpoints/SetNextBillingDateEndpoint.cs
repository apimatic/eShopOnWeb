using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public record SetNextBillingDateRequest(DateOnly? Date);

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, int, SetNextBillingDateRequest, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, MaxioGateway billing) =>
            {
                return await HandleAsync(subscriptionId, request, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("AdminBillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, MaxioGateway billing)
    {
        if (request.Date is null)
            throw new BillingException(400, "A date (YYYY-MM-DD) is required.");
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date.Value));
    }
}
