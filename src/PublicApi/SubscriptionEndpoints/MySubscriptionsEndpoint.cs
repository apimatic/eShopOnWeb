using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService service, HttpContext context, CancellationToken cancellationToken) =>
                await HandleWithCancellationAsync(service, context, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service, HttpContext context) =>
        HandleWithCancellationAsync(service, context, CancellationToken.None);

    private static async Task<IResult> HandleWithCancellationAsync(ISubscriptionService service, HttpContext context, CancellationToken cancellationToken)
    {
        var subscriptions = await service.GetMySubscriptionsAsync(context.User.Identity?.Name ?? string.Empty, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse(subscriptions.Select(SubscriptionDto.From).ToArray()));
    }
}
