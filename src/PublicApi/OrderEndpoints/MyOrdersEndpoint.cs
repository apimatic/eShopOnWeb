using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>>
{
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public MyOrdersEndpoint(IReadRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IReadRepository<Order> orderRepo) =>
            {
                var request = new MyOrdersRequest { BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty };
                return await HandleAsync(request, orderRepo);
            })
            .Produces<object>(200)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new OrdersByBuyerWithPaymentSpec(request.BuyerId);
        var orders = await orderRepo.ListAsync(spec);

        var paymentsSpec = new OrderPaymentsByBuyerSpec(request.BuyerId);
        var payments = await _paymentRepository.ListAsync(paymentsSpec);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        var result = orders.Select(o =>
        {
            paymentByOrder.TryGetValue(o.Id, out var payment);
            return new
            {
                orderId = o.Id,
                orderDate = o.OrderDate,
                total = o.Total(),
                shipToAddress = new
                {
                    o.ShipToAddress.Street,
                    o.ShipToAddress.City,
                    o.ShipToAddress.State,
                    o.ShipToAddress.Country,
                    o.ShipToAddress.ZipCode
                },
                items = o.OrderItems.Select(i => new
                {
                    productName = i.ItemOrdered.ProductName,
                    units = i.Units,
                    unitPrice = i.UnitPrice
                }),
                payment = payment is null ? null : new
                {
                    paymentStatus = payment.Status.ToString(),
                    payPalOrderId = payment.PayPalOrderId,
                    authorizationId = payment.AuthorizationId,
                    captureId = payment.CaptureId,
                    capturedAmount = payment.CapturedAmount,
                    totalRefunded = payment.TotalRefunded()
                }
            };
        });

        return Results.Ok(result);
    }
}
