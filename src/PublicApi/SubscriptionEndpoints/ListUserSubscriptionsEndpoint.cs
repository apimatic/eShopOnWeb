using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, MaxioSubscriptionService service, CancellationToken ct) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? throw new InvalidOperationException("User ID not found in token");
                var request = new EmptyRequest { UserId = userId };
                return await HandleAsync(request, service);
            })
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioSubscriptionService service)
    {
        var response = new ListUserSubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userId = request.UserId ?? throw new InvalidOperationException("User ID not found in token");
            var subscriptions = await service.GetUserSubscriptionsAsync(userId, default);
            response.Subscriptions = subscriptions;

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
    }
}
