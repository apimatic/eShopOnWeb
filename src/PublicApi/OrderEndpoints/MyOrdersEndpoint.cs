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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Read side for the caller's orders with their payment state.</summary>
public class OrderQueries
{
    private readonly IReadRepository<Order> _orders;
    private readonly IReadRepository<Payment> _payments;

    public OrderQueries(IReadRepository<Order> orders, IReadRepository<Payment> payments)
    {
        _orders = orders;
        _payments = payments;
    }

    public async Task<List<(Order Order, Payment? Payment)>> ForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var ids = orders.Select(o => o.Id).ToArray();
        var payments = ids.Length == 0
            ? new List<Payment>()
            : await _payments.ListAsync(new PaymentsByOrderIdsSpec(ids), cancellationToken);
        var byOrder = payments.ToDictionary(p => p.OrderId);
        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => (o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }
}

public class MyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// The signed-in shopper's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, OrderQueries>
{
    private readonly string _currency;

    public MyOrdersEndpoint(IOptions<PayPalOptions> payPalOptions)
    {
        _currency = payPalOptions.Value.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, OrderQueries queries, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.BuyerId(), CancellationToken = cancellationToken }, queries);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, OrderQueries queries)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var rows = await queries.ForBuyerAsync(request.BuyerId, request.CancellationToken);
        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = rows.Select(r => OrderDtoMapper.ToDto(r.Order, r.Payment, _currency)).ToList(),
        };
        return Results.Ok(response);
    }
}
