using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public List<CreateOrderItemRequest>? OrderItems { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
    public AddressRequest? ShippingAddress { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    private readonly FinixOptions _finixOptions;

    public CreateOrderEndpoint(IOptions<FinixOptions> finixOptions)
    {
        _finixOptions = finixOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var items = (request.Items ?? request.OrderItems ?? new List<CreateOrderItemRequest>())
            .Select(item => (CatalogItemId: item.ResolveCatalogItemId(), item.Quantity))
            .Where(item => item.CatalogItemId > 0)
            .ToList();

        var address = (request.ShipToAddress ?? request.ShippingAddress ?? new AddressRequest()).ToOrderAddress();
        var details = await paymentService.CreateOrderAsync(request.BuyerId!, items, address, CancellationToken.None);

        return Results.Created($"api/orders/{details.Order.Id}", details.ToOrderResponse(_finixOptions.Currency));
    }
}
