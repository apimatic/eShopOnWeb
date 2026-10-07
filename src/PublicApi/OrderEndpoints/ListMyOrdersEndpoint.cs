using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's own orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderPaymentService>
{
    private readonly IPaymentGateway _paymentGateway;

    public ListMyOrdersEndpoint(IPaymentGateway paymentGateway)
    {
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                return await HandleAsync(new ListMyOrdersRequest(buyerId), orderPaymentService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService orderPaymentService)
    {
        var orders = await orderPaymentService.GetBuyerOrdersAsync(request.BuyerId, CancellationToken.None);
        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private MyOrderDto ToDto(Order order)
    {
        var paid = order.AuthorisedPayment;
        var latest = order.LatestPaymentAttempt;
        var currency = paid?.Currency ?? latest?.Currency ?? _paymentGateway.Currency;
        return new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = currency,
            PaymentStatus = order.PaymentStatus.ToString(),
            AmountPaid = paid?.Amount ?? 0m,
            AmountRefunded = MinorUnits.FromMinorUnits(order.RefundedMinorUnits, currency),
            Items = order.OrderItems.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            LatestPayment = (paid ?? latest) is { } attempt
                ? new MyOrderPaymentDto
                {
                    Status = attempt.Status.ToString(),
                    Amount = attempt.Amount,
                    Currency = attempt.Currency,
                    PspReference = attempt.PspReference,
                    RefusalReason = attempt.RefusalReason,
                    CreatedAt = attempt.CreatedAt
                }
                : null,
            Refunds = order.Refunds.OrderBy(r => r.Sequence).Select(r => new MyOrderRefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                Currency = r.Currency,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
