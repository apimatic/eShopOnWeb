using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the caller's Maxio subscriptions. Returns an empty list if the caller has never
/// subscribed (i.e. no Maxio customer exists yet for them).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingClient maxio) =>
            {
                var request = new ListMySubscriptionsRequest { BuyerEmail = user.Identity?.Name ?? string.Empty };
                return await HandleAsync(request, maxio);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioBillingClient maxio)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await maxio.ListSubscriptionsForBuyerAsync(request.BuyerEmail);
        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
