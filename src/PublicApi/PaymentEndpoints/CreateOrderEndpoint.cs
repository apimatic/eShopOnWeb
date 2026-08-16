using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>The identifier of the newly created order.</summary>
    public int OrderId { get; set; }
    public OrderSummaryDto? Order { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items (shopper-scoped). The order starts
/// awaiting payment; the caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : null;

        try
        {
            var (order, payment) = await paymentService.PlaceOrderAsync(buyerId, lines, address);
            response.OrderId = order.Id;
            response.Order = PaymentMapper.ToSummaryDto(order, payment);
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (PaymentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
