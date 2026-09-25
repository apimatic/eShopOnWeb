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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record PlaceOrderRequest(string BuyerId, PlaceOrderBody Body, CancellationToken Ct);

/// <summary>POST /api/orders — place an order from catalog items (awaiting payment).</summary>
public class OrdersPlaceEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderBody body, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new PlaceOrderRequest(user.BuyerId(), body, ct), service))
            .Produces<PlaceOrderApiResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var lines = (request.Body.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var a = request.Body.ShipToAddress;
        var address = a is not null
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("N/A", "N/A", "N/A", "N/A", "N/A");

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address, request.Ct);
        return Results.Created($"api/orders/{result.OrderId}",
            new PlaceOrderApiResponse(result.OrderId, result.Total, result.CurrencyCode));
    }
}
