using System.Linq;
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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CreateOrderEndpoint.GetBuyerId(user);
                var card = request.Card ?? request;
                var saved = await paymentService.SavePaymentMethodAsync(buyerId, card.ToCardDetails(), cancellationToken);
                var response = PaymentMethodDto.From(saved);

                return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = CreateOrderEndpoint.GetBuyerId(user);
                var methods = await paymentService.GetPaymentMethodsAsync(buyerId, cancellationToken);
                var response = new PaymentMethodListResponse
                {
                    PaymentMethods = methods.Select(PaymentMethodDto.From).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CreateOrderEndpoint.GetBuyerId(user);
                await paymentService.DeletePaymentMethodAsync(buyerId, paymentMethodId, cancellationToken);

                return Results.Ok(new { paymentMethodId, deleted = true });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
