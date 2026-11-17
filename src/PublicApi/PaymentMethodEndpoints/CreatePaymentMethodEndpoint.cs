using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. Only Finix identifiers and display metadata are stored.
/// </summary>
public class CreatePaymentMethodEndpoint
    : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CreatePaymentMethodEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<PaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var card = new CardRequest
        {
            Number = request.Number,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            SecurityCode = request.SecurityCode,
            Name = request.Name,
            BillingAddress = request.BillingAddress
        };

        var saved = await _orderPaymentService.SavePaymentMethodAsync(
            user.Identity!.Name!, new SaveCardCommand(PaymentRequestMapper.ToCardDetails(card)),
            cancellationToken);

        var response = new PaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear,
            Name = saved.Name,
            CreatedAt = saved.CreatedAt
        };

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
