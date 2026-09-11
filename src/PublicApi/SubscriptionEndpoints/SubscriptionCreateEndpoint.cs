using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, IMaxioSubscriptionService svc, ClaimsPrincipal user) =>
        {
            var reference = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name ?? "unknown";
            var productHandle = !string.IsNullOrWhiteSpace(req.ProductHandle) ? req.ProductHandle : "eshop-pro";
            try
            {
                var sub = await svc.SubscribeAsync(reference, productHandle);
                return Results.Ok(new SubscriptionResponse
                {
                    Id = sub.Id,
                    State = sub.State,
                    ProductHandle = sub.ProductHandle,
                    Price = (double)sub.Price,
                    NextBillingDate = sub.NextBillingDate,
                    Reference = sub.Reference
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService svc)
        => Task.FromResult<IResult>(Results.Ok());
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public double Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string Reference { get; set; } = string.Empty;
}
