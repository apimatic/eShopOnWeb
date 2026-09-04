using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// The signed-in shopper's saved cards. A shopper only ever sees their own.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsResponse, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService paymentMethodService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(new ListPaymentMethodsResponse(Guid.NewGuid()), paymentMethodService, http, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsResponse request, IPaymentMethodService paymentMethodService) =>
        HandleAsync(request, paymentMethodService, httpContext: null, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListPaymentMethodsResponse request, IPaymentMethodService paymentMethodService, HttpContext? httpContext, CancellationToken ct)
    {
        var buyerId = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await paymentMethodService.ListForBuyerAsync(buyerId, ct);
        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(pm => new PaymentMethodDto
            {
                PaymentMethodId = pm.Id,
                Brand = pm.Brand,
                LastDigits = pm.LastDigits,
                Expiry = pm.Expiry
            }).ToList()
        });
    }
}
