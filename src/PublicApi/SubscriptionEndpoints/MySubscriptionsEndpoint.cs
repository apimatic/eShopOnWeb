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
/// Lists the authenticated shopper's Maxio subscriptions. Scoped entirely by the caller's own
/// JWT identity - there is no way to query another shopper's subscriptions through this route.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, string, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                var buyerEmail = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(buyerEmail, maxioClient);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerEmail, IMaxioClient maxioClient)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        var reference = SubscribeEndpoint.BuildCustomerReference(buyerEmail);
        var customer = await maxioClient.FindCustomerByReferenceAsync(reference);
        if (customer is null)
        {
            // No Maxio customer yet means no subscriptions yet - a valid, non-error state.
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.Id,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            State = s.State,
            NextBillingAt = s.NextBillingAt
        }));

        return Results.Ok(response);
    }
}
