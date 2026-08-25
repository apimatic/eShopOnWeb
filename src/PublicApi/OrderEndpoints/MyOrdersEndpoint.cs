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

/// <summary>Lists the signed-in shopper's own orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest,
    (IRepository<Order> Orders, IRepository<OrderPayment> Payments, ClaimsPrincipal User)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orders, IRepository<OrderPayment> payments, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new MyOrdersRequest(), (orders, payments, user));
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request,
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, ClaimsPrincipal User) dependency)
    {
        var response = new MyOrdersResponse(request.CorrelationId());

        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await dependency.Orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await dependency.Payments.ListAsync(new OrderPaymentsByOrderIdsSpec(orderIds));
        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);

        response.Orders = orders.Select(order =>
        {
            paymentsByOrderId.TryGetValue(order.Id, out var payment);
            return new OrderSummaryDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = payment?.Currency,
                AuthorizationStatus = payment?.AuthorizationStatus,
                CaptureStatus = payment?.CaptureStatus,
                CapturedAmount = payment?.CapturedAmount,
                RefundedAmount = payment?.RefundedAmount ?? 0m
            };
        }).ToList();

        return Results.Ok(response);
    }
}
