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
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ClaimsPrincipal>
{
    private readonly OrderPaymentService _paymentService;

    public ListMyOrdersEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await Handle(new ListMyOrdersRequest(), user, ct);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, ClaimsPrincipal user)
        => Handle(request, user, CancellationToken.None);

    private async Task<IResult> Handle(ListMyOrdersRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var buyerId = user.Identity?.Name;
            if (buyerId is null)
            {
                return Results.Unauthorized();
            }

            var orders = await _paymentService.ListMyOrdersAsync(buyerId, ct);
            var response = new ListMyOrdersResponse
            {
                Orders = orders.Select(o => new OrderDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Status = o.Status.ToString(),
                    Total = o.Total(),
                    Currency = o.Payment?.Currency ?? _paymentService.Currency,
                    Items = o.OrderItems.Select(i => new OrderItemDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Units = i.Units
                    }).ToList(),
                    Payment = o.Payment is null ? null : new PaymentDto
                    {
                        AuthorizationId = o.Payment.AuthorizationId,
                        AuthorizationStatus = o.Payment.AuthorizationStatus,
                        CaptureId = o.Payment.CaptureId,
                        CaptureStatus = o.Payment.CaptureStatus,
                        CapturedAmount = o.Payment.CapturedAmount,
                        PayPalFee = o.Payment.PayPalFee,
                        NetAmount = o.Payment.NetAmount,
                        TotalRefunded = o.Payment.TotalRefunded,
                        RefundableAmount = o.Payment.RefundableAmount,
                        PaymentMethod = o.Payment.PaymentMethodDescription,
                        Refunds = o.Payment.Refunds.Select(r => new RefundDto
                        {
                            RefundId = r.RefundId,
                            Amount = r.Amount,
                            Status = r.Status
                        }).ToList()
                    }
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
