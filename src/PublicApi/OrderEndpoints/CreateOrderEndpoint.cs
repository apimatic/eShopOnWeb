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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the caller from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<PaymentErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is { } a
            ? new Address(a.Street ?? string.Empty, a.City ?? string.Empty, a.State ?? string.Empty, a.Country ?? string.Empty, a.ZipCode ?? string.Empty)
            : null;

        var result = await paymentService.PlaceOrderAsync(request.BuyerId, lines, address, CancellationToken.None);
        if (result.Outcome != PlaceOrderOutcome.Created)
        {
            return Results.Json(new PaymentErrorResponse { StatusCode = StatusCodes.Status400BadRequest, Message = result.Message! },
                statusCode: StatusCodes.Status400BadRequest);
        }

        response.OrderId = result.Order!.Id;
        response.Order = OrderSummaryDto.From(result.Order, paymentService.Currency);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
