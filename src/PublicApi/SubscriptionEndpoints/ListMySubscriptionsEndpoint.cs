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
/// Lists the authenticated shopper's Maxio subscriptions (plan, price, state,
/// next billing date). A shopper who never subscribed gets an empty list.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, ClaimsPrincipal principal) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService, principal);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, null);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal? principal)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var (userId, _) = CreateSubscriptionEndpoint.ResolveCallerIdentity(principal);

var result = await subscriptionService.ListUserSubscriptionsAsync(userId);
        if (result.Status == Ardalis.Result.ResultStatus.Error)
        {
            response.Errors.AddRange(result.Errors.Where(m => !string.IsNullOrEmpty(m)));
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        if (result.Status == Ardalis.Result.ResultStatus.Invalid)
        {
            response.Errors.AddRange(result.ValidationErrors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
            return Results.BadRequest(response);
        }

        foreach (var subscription in result.Value)
        {
            response.Subscriptions.Add(CreateSubscriptionEndpoint.ToDto(subscription));
        }

        return Results.Ok(response);
    }
}