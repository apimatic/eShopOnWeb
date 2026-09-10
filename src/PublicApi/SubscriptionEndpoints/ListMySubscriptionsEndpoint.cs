using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions (GET api/my-subscriptions).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(user, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioBillingService billingService)
    {
        var username = user.GetUsername();
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(username);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioErrorMapper.ToResult(ex);
        }
    }
}
