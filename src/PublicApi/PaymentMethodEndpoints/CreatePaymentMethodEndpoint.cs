using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper so it can be reused for later orders.
/// Only the safe-to-display card details are returned or stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderPaymentService paymentService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());
        var buyerId = CallerIdentity.Get(_httpContextAccessor.HttpContext);
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

        var paymentMethod = await paymentService.SavePaymentMethodAsync(
            buyerId, CardPaymentMapper.ToPayPalCardDetails(request.Card), ct);

        response.PaymentMethodId = paymentMethod.Id;
        response.Brand = paymentMethod.Brand;
        response.Last4 = paymentMethod.Last4;
        response.Expiry = paymentMethod.Expiry;

        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}