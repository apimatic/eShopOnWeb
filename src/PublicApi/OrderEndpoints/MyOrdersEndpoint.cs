using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's own orders together with their payment state. Never returns another buyer's orders.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;

    public MyOrdersEndpoint(IRepository<Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity!.Name! }, orderRepository);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> orderRepository)
    {
        var response = new MyOrdersResponse(request.CorrelationId());

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds));
        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);

        response.Orders = orders.Select(o =>
        {
            paymentsByOrderId.TryGetValue(o.Id, out var payment);
            return new MyOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                PaymentStatus = payment?.Status.ToString(),
                PayPalCaptureId = payment?.PayPalCaptureId,
                CapturedAmount = payment?.CapturedAmount,
                PayPalFeeAmount = payment?.PayPalFeeAmount,
                NetAmount = payment?.NetAmount,
                RefundedAmount = payment?.RefundedAmount
            };
        }).ToList();

        return Results.Ok(response);
    }
}
