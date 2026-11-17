using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderEndpoint.FulfilOrderRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FulfilOrderEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId) =>
                await HandleAsync(new FulfilOrderRequest { OrderId = orderId }))
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var order = await _paymentService.FulfilAsync(
            request.OrderId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = order.OrderId,
            Order = order
        });
    }

    public class FulfilOrderRequest
    {
        [JsonIgnore]
        public int OrderId { get; set; }
    }

    public class FulfilOrderResponse
    {
        public int OrderId { get; set; }
        public OrderView Order { get; set; } = null!;
    }
}
