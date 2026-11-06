using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class SetNextBillingDateRequest
{
    /// <summary>The date to bill on, written YYYY-MM-DD.</summary>
    public string? Date { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, int, SetNextBillingDateRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, request, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, BillingService billing)
    {
        var date = BillingEndpointHelpers.ParseDate(request?.Date, "date");
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date));
    }
}
