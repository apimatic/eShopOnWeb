using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionApiRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (CreateSubscriptionApiRequest request,
                    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                    ISubscriptionBillingService service,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(request, idempotencyKey, service, cancellationToken))
            .Produces<SubscriptionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionApiRequest request,
        string? idempotencyKey,
        ISubscriptionBillingService service,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw BillingException.InvalidRequest("The Idempotency-Key header is required.");
        }

        return Results.Ok(await service.SubscribeAsync(
            request.ProductHandle, idempotencyKey, cancellationToken));
    }
}
