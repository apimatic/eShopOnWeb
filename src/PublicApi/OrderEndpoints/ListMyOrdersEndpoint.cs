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

/// <summary>Returns the caller's orders with payment state.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = OrderHelpers.GetBuyerId(user);
                var orders = await orderRepo.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), ct);

                var response = orders.Select(o => new OrderSummaryDto(
                    o.Id,
                    o.OrderDate,
                    o.Total(),
                    OrderHelpers.ToDto(o.Payment),
                    o.OrderItems.Select(i => new OrderItemSummaryDto(
                        i.ItemOrdered.ProductName,
                        i.Units,
                        i.UnitPrice)).ToList()
                )).ToList();

                return Results.Ok(response);
            })
            .Produces<List<OrderSummaryDto>>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}

public record EmptyRequest();
public record OrderSummaryDto(int OrderId, System.DateTimeOffset OrderDate, decimal Total, OrderPaymentDto Payment, List<OrderItemSummaryDto> Items);
public record OrderItemSummaryDto(string ProductName, int Quantity, decimal UnitPrice);
