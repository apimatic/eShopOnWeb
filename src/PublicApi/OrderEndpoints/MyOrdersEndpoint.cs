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

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<Order> orderRepo,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new CustomerOrdersSpecification(buyerId);
                var orders = await orderRepo.ListAsync(spec, ct);

                var dtos = orders.Select(o => new OrderDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Status = o.Status.ToString(),
                    Total = o.Total(),
                    Items = o.OrderItems.Select(i => new OrderItemDto
                    {
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Quantity = i.Units
                    }).ToList(),
                    Payment = o.Payment == null ? null : new PaymentDto
                    {
                        AuthorizationId = o.Payment.AuthorizationId,
                        CaptureId = o.Payment.CaptureId,
                        CapturedAmount = o.Payment.CapturedAmountValue,
                        Currency = o.Payment.CapturedAmountCurrency,
                        TotalRefunded = o.Payment.TotalRefunded
                    }
                }).ToList();

                return Results.Ok(new MyOrdersResponse { Orders = dtos });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> service)
        => throw new System.NotSupportedException();
}

public class MyOrdersRequest : BaseRequest { }

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentDto
{
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string? CapturedAmount { get; set; }
    public string? Currency { get; set; }
    public decimal TotalRefunded { get; set; }
}
