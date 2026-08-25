using System.Threading.Tasks;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly PayPalClient _payPalClient;

    public CancelOrderEndpoint(IRepository<Payment> paymentRepository, PayPalClient payPalClient)
    {
        _paymentRepository = paymentRepository;
        _payPalClient = payPalClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orderRepository);
            })
            .Produces<CancelOrderResponse>(200)
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepository)
    {
        var orderSpec = new OrderByIdWithItemsSpec(request.OrderId);
        var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
        if (order == null)
            return Results.NotFound(new { error = "Order not found." });

        if (order.Status != OrderStatus.PaymentAuthorized)
            return Results.BadRequest(new { error = $"Order status is {order.Status}. Only PaymentAuthorized orders can be cancelled." });

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null)
            return Results.Problem("Payment record not found.");

        try
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId);

            payment.VoidAuthorization();
            await _paymentRepository.UpdateAsync(payment);

            order.SetStatus(OrderStatus.Cancelled);
            await orderRepository.UpdateAsync(order);

            return Results.Ok(new CancelOrderResponse { OrderId = request.OrderId, Status = "Cancelled" });
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }
    }
}
