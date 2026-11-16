using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest
{
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<OrderDto> Orders { get; set; } = new();
}

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly CatalogContext _db;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(CatalogContext db, IOptions<FinixOptions> finixOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _finixOptions = finixOptions;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync(new MyOrdersRequest()))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _db.Orders
            .Where(o => o.BuyerId == ownerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.Payments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync();
        var refunds = await _db.PaymentRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync();

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var refundsByOrder = refunds.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse(Guid.NewGuid());
        foreach (var order in orders)
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            refundsByOrder.TryGetValue(order.Id, out var orderRefunds);
            response.Orders.Add(PaymentMapping.ToOrderDto(order, payment,
                orderRefunds ?? new List<Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.PaymentRefund>(),
                _finixOptions.Value.Currency));
        }

        return Results.Ok(response);
    }
}
