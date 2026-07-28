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
/// Lists the authenticated caller's own subscriptions (plan, price, state, next-billing date),
/// read live from Maxio via the customer reference carried in the JWT.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                var username = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(username))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest { UserReference = username }, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
    {
        var subscriptions = await billingService.ListSubscriptionsAsync(request.UserReference);

        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(CustomerSubscriptionDto.FromDomain).ToList()
        };

        return Results.Ok(response);
    }
}
