using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderApiRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest? request, HttpRequest httpRequest, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request ??= new RefundOrderApiRequest();
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.GetRequiredBuyerId(user);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, checkout);
            })
            .Produces<RefundDetailsDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderApiRequest request, IOrderCheckoutService checkout)
    {
        var result = await checkout.RefundAsync(request.BuyerId, request.OrderId, new RefundOrderCommand
        {
            IdempotencyKey = request.IdempotencyKey ?? string.Empty,
            Amount = request.Amount
        });

        return Results.Ok(result);
    }
}

public class RefundOrderApiRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
}
