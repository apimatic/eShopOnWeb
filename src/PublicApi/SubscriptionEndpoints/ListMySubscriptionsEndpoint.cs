using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userId))
                    return Results.Unauthorized();

                var customer = await maxioClient.FindCustomerByReferenceAsync(userId);
                if (customer == null)
                {
                    return Results.Ok(new ListMySubscriptionsResponse());
                }

                var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);

                var dtos = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    PlanName = s.Product?.Name ?? "Unknown",
                    PlanHandle = s.Product?.Handle,
                    Price = s.ProductPriceInCents / 100m,
                    NextBillingDate = s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CanceledAt = s.CanceledAt
                }).ToList();

                var response = new ListMySubscriptionsResponse { Subscriptions = dtos };
                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(401)
            .WithTags("SubscriptionEndpoints");
    }
}
