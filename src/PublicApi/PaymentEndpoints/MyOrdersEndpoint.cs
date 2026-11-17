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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly CatalogContext _db;

    public MyOrdersEndpoint(CatalogContext db)
    {
        _db = db;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync();
        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);

        var paymentIds = payments.Select(p => p.Id).ToList();
        var refunds = await _db.PaymentRefunds
            .Where(r => paymentIds.Contains(r.OrderPaymentId))
            .ToListAsync();
        var refundsByPaymentId = refunds
            .GroupBy(r => r.OrderPaymentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<PaymentRefund>)g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(order =>
            {
                paymentsByOrderId.TryGetValue(order.Id, out var payment);
                var orderRefunds = payment is not null
                    && refundsByPaymentId.TryGetValue(payment.Id, out var paymentRefunds)
                    ? paymentRefunds
                    : [];
                return PaymentResponseMapper.ToOrderResponse(order, payment, orderRefunds);
            }).ToList()
        };

        return Results.Ok(response);
    }
}
