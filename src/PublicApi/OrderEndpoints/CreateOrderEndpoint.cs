using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Place an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, PaymentService>
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
            (CreateOrderRequest request, PaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, PaymentService paymentService)
    {
        try
        {
            var ownerId = EndpointHelpers.GetOwnerId(_httpContextAccessor.HttpContext!.User);
            var items = request.Items
                .Select(i => new NewOrderItem(i.CatalogItemId, i.Quantity))
                .ToList();
            var shippingAddress = request.ShippingAddress?.ToAddress() ?? new AddressRequest().ToAddress();

            var (order, payment) = await paymentService.CreateOrderAsync(ownerId, items, shippingAddress);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Order = OrderPaymentDto.From(payment)
            };

            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
