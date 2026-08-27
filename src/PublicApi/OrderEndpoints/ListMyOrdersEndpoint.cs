using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders together with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var request = new ListMyOrdersRequest { BuyerId = httpContext.User.Identity?.Name ?? string.Empty };
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(ListMyOrdersRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        var orders = await paymentService.ListMyOrdersAsync(request.BuyerId, cancellationToken);

        return Results.Ok(new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Payment = o.Payment is null ? null : PaymentDto.FromEntity(o.Payment)
            }).ToList()
        });
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
