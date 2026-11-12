using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionUsageRequest : BaseRequest
{
    public SubscriptionUsageRequest(string userName, int subscriptionId, string? date)
    {
        UserName = userName;
        SubscriptionId = subscriptionId;
        Date = date;
    }

    public string UserName { get; }
    public int SubscriptionId { get; }
    public string? Date { get; }
}

/// <summary>
/// Metered usage of one of the caller's subscriptions on a single day (?date=YYYY-MM-DD)
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint<IResult, SubscriptionUsageRequest, SubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, SubscriptionBilling billing, HttpContext http) =>
            {
                return await HandleAsync(new SubscriptionUsageRequest(Caller.UserName(user), subscriptionId, date), billing, http.RequestAborted);
            })
            .Produces<DailyUsageView>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionUsageRequest request, SubscriptionBilling billing)
        => HandleAsync(request, billing, default);

    public async Task<IResult> HandleAsync(SubscriptionUsageRequest request, SubscriptionBilling billing, System.Threading.CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw MaxioProblemException.BadRequest("date is required and must be a calendar date formatted YYYY-MM-DD.");

        var usage = await billing.GetUsageAsync(request.UserName, request.SubscriptionId, day, ct);
        return Results.Ok(usage);
    }
}
