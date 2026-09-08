using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the billing subscriptions of the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly ICurrentUserContext _currentUser;

    public MySubscriptionsEndpoint(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(await billingService.ListSubscriptionsForUserAsync(user.Id));
        return Results.Ok(response);
    }
}
