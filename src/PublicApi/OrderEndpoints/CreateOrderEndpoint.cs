using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                request.BuyerId = RequireBuyerId(user);
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
        => HandleAsync(request, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var order = await orderPaymentService.PlaceOrderAsync(
            new PlaceOrderRequest(
                request.BuyerId,
                request.Items.ConvertAll(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)),
                request.ShipTo?.ToAddress()),
            cancellationToken);

        response.OrderId = order.Id;
        response.Order = OrderDtoMapper.ToDto(order, _payPalSettings.Currency);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    internal static string RequireBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new OrderPaymentException("The caller is not authenticated.", 401);
        }

        return name;
    }
}
