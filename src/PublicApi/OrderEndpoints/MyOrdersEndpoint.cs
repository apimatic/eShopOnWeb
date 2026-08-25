using System.Collections.Generic;
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

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepository,
                   IRepository<OrderPayment> paymentRepository,
                   HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                var spec = new CustomerOrdersWithItemsSpecification(buyer);
                var orders = await orderRepository.ListAsync(spec);

                var result = new List<object>();
                foreach (var order in orders)
                {
                    var paySpec = new OrderPaymentByOrderIdWithRefundsSpec(order.Id);
                    var payment = await paymentRepository.FirstOrDefaultAsync(paySpec);

                    var refundList = new List<object>();
                    if (payment != null)
                    {
                        foreach (var r in payment.Refunds)
                        {
                            refundList.Add(new
                            {
                                refundId = r.PayPalRefundId,
                                amount = r.Amount,
                                refundedAt = r.RefundedAt
                            });
                        }
                    }

                    result.Add(new
                    {
                        orderId = order.Id,
                        orderDate = order.OrderDate,
                        total = order.Total(),
                        paymentStatus = payment?.Status.ToString() ?? OrderPaymentStatus.PendingPayment.ToString(),
                        payPalOrderId = payment?.PayPalOrderId,
                        authorizationId = payment?.AuthorizationId,
                        captureId = payment?.CaptureId,
                        capturedAmount = payment?.CapturedAmount,
                        totalRefunded = payment?.TotalRefunded,
                        refunds = refundList,
                        items = GetItemSummaries(order)
                    });
                }

                return Results.Ok(result);
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);

    private static List<object> GetItemSummaries(Order order)
    {
        var items = new List<object>();
        foreach (var item in order.OrderItems)
        {
            items.Add(new
            {
                catalogItemId = item.ItemOrdered.CatalogItemId,
                productName = item.ItemOrdered.ProductName,
                unitPrice = item.UnitPrice,
                units = item.Units
            });
        }
        return items;
    }
}
