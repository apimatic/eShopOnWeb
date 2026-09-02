using System;
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
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly OrderPaymentService _paymentService;

    public CreateOrderEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await Handle(request, user, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
        => Handle(request, user, CancellationToken.None);

    private async Task<IResult> Handle(CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var buyerId = user.Identity?.Name;
            if (buyerId is null)
            {
                return Results.Unauthorized();
            }

            var address = new Address(
                request.Street ?? "N/A",
                request.City ?? "N/A",
                request.State ?? "N/A",
                request.Country ?? "N/A",
                request.ZipCode ?? "N/A");

            var order = await _paymentService.CreateOrderAsync(buyerId,
                request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList(), address, ct);

            return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = _paymentService.Currency
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
