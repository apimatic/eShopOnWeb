using System.Globalization;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SetNextBillingDateRequest
{
    /// <summary>The date to bill next, YYYY-MM-DD.</summary>
    public string? Date { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, IBillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(request?.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw BillingException.BadRequest("date is required and must be formatted YYYY-MM-DD.");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, day, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
