using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: cancels an order before fulfilment. If funds were held, the hold is
/// released via PayPal's void so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new CancelOrderRequest(orderId);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, PaymentDependencies deps)
    {
        var response = new CancelOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        var order = await deps.OrderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await deps.PaymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null)
        {
            return Results.Problem("This order has no associated payment record.", statusCode: 500);
        }

        // Idempotent in effect.
        if (order.Status == OrderStatus.Cancelled)
        {
            response.OrderStatus = order.Status.ToString();
            return Results.Ok(response);
        }

        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            return Results.Conflict($"Order {order.Id} cannot be cancelled from status {order.Status}.");
        }

        if (payment.Status == PaymentStatus.Authorized && payment.PayPalAuthorizationId != null)
        {
            try
            {
                await deps.PayPalClient.VoidAuthorizationAsync(payment.PayPalAuthorizationId);
            }
            catch (PayPalApiException ex)
            {
                return Results.Problem(ex.Message, statusCode: 502, title: ex.ErrorName ?? "Could not release the payment hold");
            }
            payment.RecordVoid();
            await deps.PaymentRepository.UpdateAsync(payment);
        }

        order.MarkCancelled();
        await deps.OrderRepository.UpdateAsync(order);

        response.OrderStatus = order.Status.ToString();
        return Results.Ok(response);
    }
}
