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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequestItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderRequestItem> Items { get; set; } = new();
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>POST /api/orders — place an order from catalog items. Starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                IPaymentCurrencyProvider currency,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var lines = (request.Items ?? new List<PlaceOrderRequestItem>())
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                var orderId = await paymentService.PlaceOrderAsync(buyerId, lines, ct);
                var orders = await paymentService.GetOrdersForBuyerAsync(buyerId, ct);
                var placed = orders.FirstOrDefault(o => o.Order.Id == orderId);

                var response = new PlaceOrderResponse
                {
                    OrderId = orderId,
                    Status = placed?.Payment?.Status.ToString() ?? "PendingPayment",
                    Total = placed?.Order.Total() ?? 0m,
                    Currency = currency.CurrencyCode
                };
                return Results.Created($"api/orders/{orderId}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
