using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List User Subscriptions
/// </summary>
public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, ISubscriptionService service) =>
            {
                return await HandleAsync(new ListUserSubscriptionsRequest(), service, user);
            })
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request, ISubscriptionService service)
    {
        throw new NotImplementedException("Use overload with user parameter");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request, ISubscriptionService service, ClaimsPrincipal user)
    {
        try
        {
            var response = new ListUserSubscriptionsResponse(request.CorrelationId());

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await service.ListUserSubscriptionsAsync(userId);
            response.Subscriptions = subscriptions;

            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListUserSubscriptionsRequest : BaseRequest
{
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
