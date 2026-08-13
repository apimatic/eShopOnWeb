using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used if omitted (shipping is not part of this feature).</summary>
    public PlaceOrderAddress? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; private set; } = string.Empty;

    public void SetBuyerId(string buyerId) => BuyerId = buyerId;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items (reusing the app's existing order
/// model), then tells the shopper their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                request.SetBuyerId(user.GetBuyerId());
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var lines = (request.Items ?? new List<PlaceOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        AddressData? address = request.ShipToAddress is null
            ? null
            : new AddressData(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address, cancellationToken);
        if (!result.Success)
            return Results.BadRequest(result.Error);

        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = result.OrderId!.Value };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
