using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();
    public AddressDto ShipToAddress { get; set; } = new();

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Place an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.BuyerId = PaymentMappers.BuyerId(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService service, CancellationToken ct)
    {
        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = new Address(
            request.ShipToAddress.Street,
            request.ShipToAddress.City,
            request.ShipToAddress.State,
            request.ShipToAddress.Country,
            request.ShipToAddress.ZipCode);

        var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, address, ct);

        return Results.Created($"api/orders/{orderId}", new CreateOrderResponse
        {
            OrderId = orderId,
            Status = "AwaitingPayment"
        });
    }
}
