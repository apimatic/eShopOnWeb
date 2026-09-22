using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order from catalog items for the signed-in shopper (awaiting payment).</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, HttpContext http)
    {
        try
        {
            var buyerId = PaymentEndpointHelpers.GetBuyerId(http);
            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();
            var lines = (request.Items ?? new List<PlaceOrderLine>())
                .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                .ToList();

            var result = await svc.PlaceOrderAsync(buyerId, lines, http.RequestAborted);
            return Results.Created($"api/orders/{result.OrderId}", new PlaceOrderResponse
            {
                OrderId = result.OrderId,
                Total = result.Total,
                CurrencyCode = result.CurrencyCode,
            });
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}

public class PlaceOrderRequest
{
    public List<PlaceOrderLine>? Items { get; set; }
}

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}
