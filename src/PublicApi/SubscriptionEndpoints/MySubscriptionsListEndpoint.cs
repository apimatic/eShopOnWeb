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
/// Lists the Maxio subscriptions belonging to the authenticated user.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(user.Identity?.Name ?? string.Empty), maxioSubscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await maxioSubscriptionService.GetSubscriptionsAsync(request.Username);

        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            CreatedAt = s.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
