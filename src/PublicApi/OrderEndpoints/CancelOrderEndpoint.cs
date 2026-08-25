using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IPayPalGateway _paypal;

    public CancelOrderEndpoint(IRepository<Order> orderRepo, IPayPalGateway paypal)
    {
        _orderRepo = orderRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, int orderId) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(orderId, buyerId, ctx.RequestAborted);
            })
            .Produces<CancelOrderResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(int orderId, string buyerId, System.Threading.CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null || order.BuyerId != buyerId) return Results.NotFound();

        if (order.PaymentStatus == PaymentStatus.Cancelled)
            return Results.Ok(new CancelOrderResponse { Status = order.PaymentStatus.ToString() });

        if (order.PaymentStatus == PaymentStatus.Fulfilled)
            return Results.Problem("Cannot cancel a fulfilled order. Use the refund endpoint instead.", statusCode: 409);

        if (order.PaymentStatus == PaymentStatus.Authorized)
        {
            try
            {
                await _paypal.VoidAsync(order.PayPalAuthorizationId!, ct);
            }
            catch (PayPalException ex)
            {
                return Results.Problem($"Void failed: {ex.Message}", statusCode: 502);
            }
        }

        order.MarkCancelled();
        await _orderRepo.UpdateAsync(order, ct);

        return Results.Ok(new CancelOrderResponse { Status = order.PaymentStatus.ToString() });
    }
}

public class CancelOrderResponse
{
    public string Status { get; set; } = string.Empty;
}
