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
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Lists the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "List the caller's orders with payment state", Tags = new[] { "Orders" })]
        async (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(user, service, ct))
            .Produces<IReadOnlyList<MyOrderResponse>>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(buyerId, ct);
        var response = orders.Select(Map).ToList();
        return Results.Ok(response);
    }

    private static MyOrderResponse Map(OrderWithPayment ow)
    {
        var items = ow.Order.OrderItems
            .Select(i => new MyOrderItem(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        MyOrderPayment? payment = ow.Payment is null ? null : MapPayment(ow.Payment);

        return new MyOrderResponse(ow.Order.Id, ow.Order.OrderDate, ow.Order.Total(), items, payment);
    }

    private static MyOrderPayment MapPayment(OrderPayment p) => new(
        p.Status.ToString(),
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.Amount,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.CurrencyCode,
        p.Refunds.Select(r => new MyOrderRefund(r.PayPalRefundId, r.Amount, r.Status)).ToList());
}
