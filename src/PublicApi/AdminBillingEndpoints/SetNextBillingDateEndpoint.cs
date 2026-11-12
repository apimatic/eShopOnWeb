using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminBillingEndpoints;

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>The date the subscriber is next billed, formatted YYYY-MM-DD.</summary>
    public string? NextBillingDate { get; set; }

    /// <summary>Alias of <see cref="NextBillingDate"/>.</summary>
    public string? Date { get; set; }

    internal int SubscriptionId { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, SetNextBillingDateRequest, SubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, SubscriptionBilling billing, HttpContext http) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, billing, http.RequestAborted);
            })
            .Produces<SubscriptionView>()
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(SetNextBillingDateRequest request, SubscriptionBilling billing)
        => HandleAsync(request, billing, default);

    public async Task<IResult> HandleAsync(SetNextBillingDateRequest request, SubscriptionBilling billing, System.Threading.CancellationToken ct)
    {
        var text = !string.IsNullOrWhiteSpace(request.NextBillingDate) ? request.NextBillingDate : request.Date;
        if (!DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw MaxioProblemException.BadRequest("nextBillingDate is required and must be a calendar date formatted YYYY-MM-DD.");

        var view = await billing.SetNextBillingDateAsync(request.SubscriptionId, date, ct);
        return Results.Ok(view);
    }
}
