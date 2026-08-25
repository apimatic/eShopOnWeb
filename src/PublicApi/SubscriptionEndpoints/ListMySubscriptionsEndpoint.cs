using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions, sourced from Maxio via the
/// customer record whose reference matches the caller's username.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, IMaxioClient maxioClient) =>
            {
                return await HandleAsync(claimsPrincipal, maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal claimsPrincipal, IMaxioClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse();

        var username = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var customer = await maxioClient.FindCustomerByReferenceAsync(username);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromMaxio));

        return Results.Ok(response);
    }
}
