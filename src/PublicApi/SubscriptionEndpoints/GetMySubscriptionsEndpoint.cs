using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest _, IMaxioSubscriptionService service)
    {
        return HandleInternalAsync(service);
    }

    private static async Task<IResult> HandleInternalAsync(IMaxioSubscriptionService service)
    {
        var subscriptions = await service.GetUserSubscriptionsAsync("system-user");
        var response = new GetMySubscriptionsResponse
        {
            Subscriptions = subscriptions
        };
        return Results.Ok(response);
    }

    private static string? GetUserIdFromContext(HttpContext httpContext)
    {
        var principal = httpContext.User;
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               principal?.FindFirst("sub")?.Value ??
               principal?.FindFirst(ClaimTypes.Email)?.Value;
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public SubscriptionDto[] Subscriptions { get; set; } = [];
}
