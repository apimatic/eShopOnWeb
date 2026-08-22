using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest? request, HttpRequest httpRequest, ClaimsPrincipal user, IOrderCheckoutService checkout) =>
            {
                request ??= new RefundOrderRequest();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(orderId, request, user, checkout);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout)
        => Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout)
    {
        var buyerId = CheckoutHttp.BuyerId(user);
        var refund = await checkout.RefundAsync(buyerId, orderId, request.IdempotencyKey ?? string.Empty, request.Amount);
        var response = CheckoutHttp.ToRefundResponse(refund, orderId);
        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest
{
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
}
