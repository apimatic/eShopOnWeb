using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Places an order from catalog items for the signed-in shopper; it starts awaiting payment.</summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.BuyerId = CallerIdentity.BuyerId(http);
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var a = request.ShipToAddress;
        var address = new Address(
            a?.Street ?? "N/A", a?.City ?? "N/A", a?.State ?? "N/A", a?.Country ?? "US", a?.ZipCode ?? "00000");

        var lines = request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();
        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address, request.Ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Total = result.Total,
            Currency = result.Currency,
            Status = result.Status.ToString(),
        };
        return Results.Created($"api/orders/{result.OrderId}", response);
    }
}
