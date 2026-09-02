using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public ListPaymentMethodsEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var paymentMethods = await _paymentService.ListPaymentMethodsAsync(buyerId);

        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = paymentMethods.Select(p => new PaymentMethodDto
            {
                PaymentMethodId = p.Id,
                Brand = p.Brand,
                LastDigits = p.LastDigits,
                Expiry = p.Expiry,
                CardholderName = p.CardholderName,
                CreatedAt = p.CreatedAt
            }).ToList()
        });
    }
}
