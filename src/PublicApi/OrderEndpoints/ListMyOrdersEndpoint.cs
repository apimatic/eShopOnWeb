using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersRequest : BaseRequest { }

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<MyOrderRefundDto> Refunds { get; set; } = new();
}

public class MyOrderRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orders, CancellationToken ct) =>
            {
                var list = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(CallerIdentity.BuyerId(user)), ct);
                var response = new ListMyOrdersResponse
                {
                    Orders = list.Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Currency = o.Currency ?? string.Empty,
                        PaymentStatus = o.PaymentStatus.ToString(),
                        PayPalOrderId = o.PayPalOrderId,
                        AuthorizationId = o.PayPalAuthorizationId,
                        AuthorizationStatus = o.PayPalAuthorizationStatus,
                        CaptureId = o.PayPalCaptureId,
                        CaptureStatus = o.PayPalCaptureStatus,
                        CapturedAmount = o.CapturedAmount,
                        PaypalFee = o.PaypalFee,
                        NetAmount = o.NetAmount,
                        RefundedAmount = o.RefundedAmount,
                        Refunds = o.Refunds.Select(r => new MyOrderRefundDto
                        {
                            RefundId = r.PayPalRefundId,
                            Amount = r.Amount,
                            Status = r.Status
                        }).ToList()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IReadRepository<Order> orders) =>
        Task.FromResult(Results.BadRequest());
}
