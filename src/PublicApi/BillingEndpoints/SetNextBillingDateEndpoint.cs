using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>The date to bill on, YYYY-MM-DD.</summary>
    public string? Date { get; set; }
    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }

    internal string? ResolvedDate => string.IsNullOrWhiteSpace(Date) ? NextBillingDate : Date;
}

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, MaxioBillingService billing) =>
            {
                return await HandleAsync(subscriptionId, request, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, MaxioBillingService billing)
    {
        if (!DateOnly.TryParseExact(request.ResolvedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw BillingException.BadRequest("A 'date' in the form YYYY-MM-DD is required.");
        return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date));
    }
}
