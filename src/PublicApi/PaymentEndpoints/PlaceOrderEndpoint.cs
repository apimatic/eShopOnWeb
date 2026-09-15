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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders — a signed-in shopper places an order from catalog items. The order starts awaiting
/// payment; its identifier is returned as a top-level <c>orderId</c>.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                IRepository<Payment> paymentRepository,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                var lines = request.Items
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = BuildAddress(request.ShipToAddress);

                var orderId = await service.PlaceOrderAsync(buyerId, lines, address, ct);

                var payment = await paymentRepository.FirstOrDefaultAsync(
                    new ApplicationCore.Specifications.PaymentByOrderIdSpecification(orderId), ct);

                var response = new PlaceOrderResponse(request.CorrelationId())
                {
                    OrderId = orderId,
                    Payment = payment is null ? new PaymentStateDto() : PaymentStateDto.From(payment)
                };
                return Results.Created($"api/orders/{orderId}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        if (dto is null)
        {
            // No storefront UI collects an address here; default a placeholder so the flow is drivable.
            return new Address("123 Main St", "Redmond", "WA", "USA", "98052");
        }

        return new Address(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);
    }
}
