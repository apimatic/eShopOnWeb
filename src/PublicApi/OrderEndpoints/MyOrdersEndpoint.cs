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

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new OrdersByBuyerSpec(buyerId);
                var orders = await orderRepository.ListAsync(spec, ct);

                var dtos = orders.Select(o => new OrderDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Total = o.Total(),
                    Status = o.Status.ToString(),
                    PayPalOrderId = o.PayPalOrderId,
                    AuthorizationId = o.PayPalAuthorizationId,
                    CaptureId = o.PayPalCaptureId,
                    CapturedAmount = o.CapturedAmount,
                    PayPalFee = o.PayPalFee,
                    NetAmount = o.NetAmount,
                    TotalRefunded = o.TotalRefunded,
                    Items = o.OrderItems.Select(i => new OrderItemDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Quantity = i.Units
                    }).ToList()
                }).ToList();

                return Results.Ok(new MyOrdersResponse { Orders = dtos });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class MyOrdersRequest : BaseRequest { }

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse() : base(System.Guid.NewGuid()) { }
    public List<OrderDto> Orders { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
