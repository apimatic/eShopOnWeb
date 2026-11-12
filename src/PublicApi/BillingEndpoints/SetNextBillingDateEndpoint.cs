using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>The date, as YYYY-MM-DD.</summary>
    public string? Date { get; set; }
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, int, SetNextBillingDateRequest>
{
    private readonly SubscriptionService _subscriptions;

    public SetNextBillingDateEndpoint(SubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SetNextBillingDateRequest request) =>
            {
                return await HandleAsync(subscriptionId, request);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request)
    {
        var text = (request.Date ?? request.NextBillingDate)?.Trim();
        var day = default(DateOnly);
        if (text is null || !(BillingHelpers.TryParseDay(text, out day) || (text.Length > 10 && BillingHelpers.TryParseDay(text[..10], out day))))
        {
            throw new BillingException(400, "The date is required, as YYYY-MM-DD.");
        }

        return Results.Ok(await _subscriptions.SetNextBillingDateAsync(subscriptionId, day));
    }
}
