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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for catalog items on behalf of the caller. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    // Same default the storefront checkout uses.
    private static readonly Address DefaultShipToAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                request.BuyerId = OrderEndpointUser.RequireName(user);
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();
        var order = await paymentService.PlaceOrderAsync(request.BuyerId, lines, ToAddress(request.ShipToAddress), request.CancellationToken);

        response.OrderId = order.Id;
        response.Total = order.Total();
        response.Currency = paymentService.Currency;
        response.PaymentStatus = order.PaymentStatus().ToString();
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(ShippingAddressDto? dto)
    {
        if (dto is null) return DefaultShipToAddress;

        if (string.IsNullOrWhiteSpace(dto.Street) || string.IsNullOrWhiteSpace(dto.City) ||
            string.IsNullOrWhiteSpace(dto.Country) || string.IsNullOrWhiteSpace(dto.ZipCode))
        {
            throw new PaymentValidationException("shipToAddress needs street, city, country and zipCode.");
        }
        if (dto.Street.Length > 180 || dto.City.Length > 100 || dto.State?.Length > 60 || dto.Country.Length > 90 || dto.ZipCode.Length > 18)
        {
            throw new PaymentValidationException("shipToAddress has a field that is too long.");
        }
        return new Address(dto.Street, dto.City, dto.State ?? string.Empty, dto.Country, dto.ZipCode);
    }
}
