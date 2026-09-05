using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling shopper's own subscriptions (empty if they have never subscribed).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionEnrollmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionEnrollmentService enrollmentService) =>
            {
                var buyer = await BuyerResolver.ResolveAsync(user, userManager);
                if (buyer is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest(buyer.Reference), enrollmentService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionEnrollmentService enrollmentService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await enrollmentService.GetSubscriptionsForBuyerAsync(request.BuyerReference);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));

        return Results.Ok(response);
    }
}
