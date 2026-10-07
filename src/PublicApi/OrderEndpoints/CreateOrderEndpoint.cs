using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the caller from catalog items at catalog prices. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var lines = (request.Items ?? new()).Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is { } a ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode) : null;

        var result = await orderPaymentService.PlaceOrderAsync(request.BuyerId, lines, address, CancellationToken.None);
        if (result.Status != PlaceOrderStatus.Created)
            return OrderEndpointResults.Error(StatusCodes.Status400BadRequest, result.Message ?? "The order is invalid.");

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId!.Value,
            Total = result.Total!.Value,
            Currency = result.Currency!,
            PaymentStatus = OrderPaymentStatus.AwaitingPayment.ToString()
        };
        return Results.Created($"api/my-orders", response);
    }
}
