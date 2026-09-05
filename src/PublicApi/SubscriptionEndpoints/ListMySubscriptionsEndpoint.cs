using System.Linq;
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
/// Lists the calling user's subscriptions, read live from Maxio. Returns an empty list
/// (rather than an error) for a user who has never subscribed, since no Maxio customer
/// exists yet for them.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                var request = new ListMySubscriptionsRequest { CustomerEmail = httpContext.User.Identity!.Name! };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var customer = await maxioClient.FindCustomerByReferenceAsync(request.CustomerEmail);
        if (customer is not null)
        {
            var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                MaxioSubscriptionId = s.Id,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                State = s.State,
                NextBillingAt = s.NextBillingAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            }).ToList();
        }

        return Results.Ok(response);
    }
}
