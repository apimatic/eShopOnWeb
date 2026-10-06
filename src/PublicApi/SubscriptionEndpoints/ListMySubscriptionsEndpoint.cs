using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions, with live state confirmed
/// against Maxio Advanced Billing.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, SubscriptionEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (SubscriptionEndpointServices services) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), services);
            })
            .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, SubscriptionEndpointServices services)
    {
        var user = await CreateSubscriptionEndpoint.ResolveUserAsync(services, services.UserManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await services.SubscriptionService.GetSubscriptionsForUserAsync(user.Id);
            var response = new ListMySubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.ToDto));
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Json(new
            {
                correlationId = request.CorrelationId(),
                error = $"The billing system is unavailable: {ex.ResponseBody}"
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
