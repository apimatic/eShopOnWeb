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

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor) :
    IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                async (
                    CreateSubscriptionRequest request,
                    ISubscriptionBillingService service) =>
                    await HandleAsync(request, service))
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<UserSubscription>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService service)
    {
        var subscription = await service.SubscribeAsync(
            request.ProductHandle,
            request.IdempotencyKey,
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return Results.Ok(subscription);
    }
}
