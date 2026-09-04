using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places a new order from catalog items; the order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
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
            (CreateOrderRequest request, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var buyerId = CallerIdentity.Get(_httpContextAccessor.HttpContext);
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

        var shipTo = new Address(
            request.ShipToAddress.Street,
            request.ShipToAddress.City,
            request.ShipToAddress.State,
            request.ShipToAddress.Country,
            request.ShipToAddress.ZipCode);

        var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity)).ToList();

        var order = await paymentService.CreateOrderAsync(buyerId, lines, shipTo, ct);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.Currency = order.Currency;

        return Results.Created($"api/orders/{order.Id}", response);
    }
}