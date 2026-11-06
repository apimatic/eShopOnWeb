using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>The date to bill the subscriber next, as YYYY-MM-DD.</summary>
    public string? Date { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, request, billing, ct);
            })
            .Produces<NextBillingDateDto>()
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, IBillingService billing, CancellationToken ct)
    {
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct));
    }
}
