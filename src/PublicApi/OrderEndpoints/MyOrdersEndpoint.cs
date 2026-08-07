using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the authenticated shopper's orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IReadRepository<Order> orderRepository) => await HandleAsync(http, orderRepository))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IReadRepository<Order> orderRepository)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);

        var orders = await orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), http.RequestAborted);

        var response = new MyOrdersResponse(Guid.NewGuid())
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Currency = "USD",
                PaymentStatus = order.PaymentStatus.ToString(),
                PayPalOrderId = order.PayPalOrderId,
                CaptureId = order.PaymentCaptureId,
                RefundId = order.PaymentRefundId,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
