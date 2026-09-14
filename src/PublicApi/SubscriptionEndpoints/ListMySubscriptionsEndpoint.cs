using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the caller's (from the bearer token) subscriptions.</summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var buyerEmail = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? string.Empty;
                return await HandleAsync(new ListMySubscriptionsRequest(buyerEmail), billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            return Results.Unauthorized();
        }

        try
        {
            var response = new ListMySubscriptionsResponse(request.CorrelationId());
            var subscriptions = await billingService.GetSubscriptionsForBuyerAsync(request.BuyerEmail);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                State = s.State,
                PriceInCents = s.PriceInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                CreatedAt = s.CreatedAt
            }));

            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: (int)HttpStatusCode.BadGateway, title: "Maxio API error");
        }
    }
}
