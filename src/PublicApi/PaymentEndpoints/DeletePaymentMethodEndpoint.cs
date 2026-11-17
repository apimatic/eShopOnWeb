using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.DeletePaymentMethodRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int paymentMethodId) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }))
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        await _paymentService.DeletePaymentMethodAsync(
            buyerId,
            request.PaymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Ok(new DeletePaymentMethodResponse
        {
            PaymentMethodId = request.PaymentMethodId,
            Deleted = true
        });
    }

    public class DeletePaymentMethodRequest
    {
        [JsonIgnore]
        public int PaymentMethodId { get; set; }
    }

    public class DeletePaymentMethodResponse
    {
        public int PaymentMethodId { get; set; }
        public bool Deleted { get; set; }
    }
}
