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
/// Lists the calling user's Maxio subscriptions. Returns an empty list if the user has
/// never subscribed (no Maxio customer exists yet for their account).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioClient maxioClient) =>
            {
                var request = new ListMySubscriptionsRequest { UserEmail = user.Identity?.Name ?? string.Empty };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.UserEmail))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await maxioClient.GetSubscriptionsForCustomerAsync(request.UserEmail);

        response.Subscriptions = subscriptions.Select(s => new MySubscriptionDto
        {
            SubscriptionId = s.Id,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.PriceInCents / 100m,
            State = s.State,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingDate = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt,
        }).ToList();

        return Results.Ok(response);
    }
}
