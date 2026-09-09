using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment; it reuses the app's existing Order/OrderItem model.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = PaymentMapping.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = request.ShipToAddress is null
            ? null
            : new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var result = await paymentService.PlaceOrderAsync(request.BuyerId, lines, address);

        response.OrderId = result.OrderId;
        response.Total = result.Total;
        response.CurrencyCode = result.CurrencyCode;
        response.Status = result.Status;
        return Results.Created($"api/orders/{result.OrderId}", response);
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the caller's token; not accepted from the body.</summary>
    internal string BuyerId { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
