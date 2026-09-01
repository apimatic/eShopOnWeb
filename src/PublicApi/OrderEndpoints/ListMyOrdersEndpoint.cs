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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public ListMyOrdersEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var orders = await _paymentService.ListBuyerOrdersAsync(buyerId, CancellationToken.None);
        var payments = (await _paymentService.ListBuyerPaymentsAsync(buyerId, CancellationToken.None))
            .ToDictionary(p => p.OrderId);

        var response = new ListMyOrdersResponse(request.CorrelationId());
        response.Orders = orders.Select(o => new MyOrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            Items = o.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payments.TryGetValue(o.Id, out var payment) ? PaymentDto.FromModel(payment) : null
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
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
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}
