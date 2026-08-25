using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _paypal;

    public CancelOrderEndpoint(IPayPalService paypal) => _paypal = paypal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId, BuyerId = user.Identity?.Name ?? "" }, orderRepo);
            })
            .Produces<CancelOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new OrderWithRefundsSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);
        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound();

        // Idempotency: already cancelled
        if (order.Status == OrderStatus.Cancelled)
            return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });

        if (order.Status == OrderStatus.Fulfilled)
            return Results.BadRequest(new { error = "Cannot cancel a fulfilled order. Use refund instead." });

        if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.Cancel();
            await orderRepo.UpdateAsync(order);
            return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });
        }

        if (order.Status != OrderStatus.PaymentAuthorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            return Results.BadRequest(new { error = $"Cannot cancel order in status {order.Status}." });

        try
        {
            await _paypal.VoidAsync(order.PayPalAuthorizationId, CancellationToken.None);
            order.Cancel();
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new CancelOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = "";
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string? Status { get; set; }
}
