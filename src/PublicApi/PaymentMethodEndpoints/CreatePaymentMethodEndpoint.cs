using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request,
                   IPayPalService payPal,
                   IRepository<SavedPaymentMethod> repo,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.CardNumber))
                    return Results.BadRequest(new { error = "CardNumber is required." });
                if (string.IsNullOrWhiteSpace(request.CardExpiry))
                    return Results.BadRequest(new { error = "CardExpiry is required (YYYY-MM)." });
                if (string.IsNullOrWhiteSpace(request.CardCvv))
                    return Results.BadRequest(new { error = "CardCvv is required." });

                var cardDetails = new PayPalCardDetails
                {
                    Number = request.CardNumber,
                    Expiry = request.CardExpiry,
                    SecurityCode = request.CardCvv,
                    CardholderName = request.CardholderName
                };

                PayPalVaultResult vaultResult;
                try
                {
                    vaultResult = await payPal.VaultCardAsync(buyerId, cardDetails, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Failed to vault card.");
                }

                var method = new SavedPaymentMethod(
                    buyerId,
                    vaultResult.PaymentTokenId,
                    vaultResult.PayPalCustomerId,
                    vaultResult.Last4,
                    vaultResult.Brand,
                    vaultResult.Expiry);

                await repo.AddAsync(method, ct);

                return Results.Ok(new CreatePaymentMethodResponse
                {
                    PaymentMethodId = method.Id,
                    Last4 = vaultResult.Last4,
                    Brand = vaultResult.Brand,
                    Expiry = vaultResult.Expiry,
                    PayPalCustomerId = vaultResult.PayPalCustomerId
                });
            })
            .Produces<CreatePaymentMethodResponse>()
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPayPalService service)
        => throw new NotImplementedException();
}
