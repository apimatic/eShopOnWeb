using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the calling shopper from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    // The storefront checkout ships every order to this demo address (see Web/Pages/Basket/Checkout).
    private static readonly ShippingAddressDto DefaultAddress = new()
    {
        Street = "123 Main St.", City = "Kent", State = "OH", Country = "United States", ZipCode = "44240"
    };

    private readonly AdyenSettings _paymentSettings;

    public CreateOrderEndpoint(IOptions<AdyenSettings> paymentSettings)
    {
        _paymentSettings = paymentSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var address = request.ShipToAddress ?? DefaultAddress;
        if (new[] { address.Street, address.City, address.Country, address.ZipCode }.Any(string.IsNullOrWhiteSpace))
        {
            return BadRequest("shipToAddress needs street, city, country and zipCode.");
        }

        Order order;
        try
        {
            order = await orderService.CreateOrderAsync(
                request.BuyerId,
                (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList(),
                new Address(address.Street, address.City, address.State, address.Country, address.ZipCode));
        }
        catch (OrderValidationException ex)
        {
            return BadRequest(ex.Message);
        }

        response.OrderId = order.Id;
        response.Order = OrderSummaryDto.From(order, _paymentSettings.NormalizedCurrency);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static IResult BadRequest(string message) =>
        Results.Json(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message },
            statusCode: StatusCodes.Status400BadRequest);
}
