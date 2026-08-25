using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record OrderItemDto(string ProductName, decimal UnitPrice, int Quantity);

public record PaymentDto(
    string PayPalOrderId,
    string AuthorizationId,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    int RefundCount,
    decimal TotalRefunded);

public record OrderDto(
    int OrderId,
    string BuyerId,
    string OrderDate,
    decimal Total,
    string Status,
    List<OrderItemDto> Items,
    PaymentDto? Payment);

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public List<OrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var orders = await orderRepo.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), ct);
                var dtos = new List<OrderDto>();
                foreach (var o in orders)
                {
                    var items = new List<OrderItemDto>();
                    foreach (var i in o.OrderItems)
                        items.Add(new OrderItemDto(i.ItemOrdered.ProductName, i.UnitPrice, i.Units));

                    PaymentDto? paymentDto = null;
                    if (o.Payment != null)
                    {
                        var p = o.Payment;
                        paymentDto = new PaymentDto(
                            p.PayPalOrderId, p.AuthorizationId, p.CaptureId,
                            p.CapturedAmount, p.PayPalFee, p.NetAmount,
                            p.Refunds.Count, p.TotalRefunded);
                    }

                    dtos.Add(new OrderDto(o.Id, o.BuyerId, o.OrderDate.ToString("O"),
                        o.Total(), o.Status.ToString(), items, paymentDto));
                }

                var response = new ListMyOrdersResponse(Guid.NewGuid()) { Orders = dtos };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
