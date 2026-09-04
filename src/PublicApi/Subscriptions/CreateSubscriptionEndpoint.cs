using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                SubscribeRequest request,
                ISubscriptionBillingService service) =>
            await HandleAsync(request, service))
            .RequireAuthorization()
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriptionBillingService service)
    {
        var context = _httpContextAccessor.HttpContext!;
        var subscription = await service.SubscribeAsync(
            context.User,
            request.ProductHandle,
            context.RequestAborted);
        return Results.Ok(subscription);
    }
}
