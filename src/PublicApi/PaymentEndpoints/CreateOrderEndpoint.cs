using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderEndpoint.CreateOrderRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateOrderRequest request) =>
                await HandleAsync(request))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        var items = request.Items
            .Select(i => new NewOrderItem(i.CatalogItemId, i.Quantity != 0 ? i.Quantity : i.Units))
            .ToList();

        var address = request.ShipToAddress ?? request.ShippingAddress;
        var order = await _paymentService.CreateOrderAsync(
            buyerId,
            items,
            address?.Street,
            address?.City,
            address?.State,
            address?.Country,
            address?.ZipCode,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Created($"api/orders/{order.OrderId}", new CreateOrderResponse
        {
            OrderId = order.OrderId,
            Order = order
        });
    }

    public class CreateOrderRequest
    {
        public List<CreateOrderItemRequest> Items { get; set; } = new();
        public ShipToAddressRequest? ShipToAddress { get; set; }
        public ShipToAddressRequest? ShippingAddress { get; set; }
    }

    public class CreateOrderItemRequest
    {
        public int CatalogItemId { get; set; }
        public int Quantity { get; set; }
        public int Units { get; set; }
    }

    public class ShipToAddressRequest
    {
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? ZipCode { get; set; }
    }

    public class CreateOrderResponse
    {
        public int OrderId { get; set; }
        public OrderView Order { get; set; } = null!;
    }
}


