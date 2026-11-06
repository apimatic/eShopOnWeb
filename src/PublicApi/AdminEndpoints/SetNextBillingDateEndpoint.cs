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

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class SetNextBillingDateRequest
{
    /// <summary>The date the subscriber is next billed, YYYY-MM-DD.</summary>
    public string? Date { get; set; }
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, int, SetNextBillingDateRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SetNextBillingDateRequest request, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, request, billing))
            .Produces<SubscriptionDto>()
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, MaxioBillingService billing)
    {
        var text = request.Date ?? request.NextBillingDate;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            throw BillingException.BadRequest("The date is required and must look like YYYY-MM-DD.");
        }
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date));
    }
}
