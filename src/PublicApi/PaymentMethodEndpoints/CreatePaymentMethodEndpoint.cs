using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Vaults a card for the authenticated shopper.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IRepository<PaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request,
                IRepository<PaymentMethod> pmRepo,
                IPayPalPaymentService payPalService,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = GetBuyerId(user);

                var card = new CardDetails(
                    request.Number,
                    request.Expiry,
                    request.SecurityCode,
                    request.Name,
                    request.CountryCode ?? "US");

                VaultResult vaultResult;
                try
                {
                    vaultResult = await payPalService.VaultCardAsync(buyerId, card, ct);
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                var pm = new PaymentMethod(buyerId, vaultResult.VaultId, vaultResult.LastFour, vaultResult.Brand, vaultResult.Expiry, vaultResult.Name);
                pm = await pmRepo.AddAsync(pm, ct);

                return Results.Created($"api/payment-methods/{pm.Id}",
                    new CreatePaymentMethodResponse(pm.Id, pm.LastFour, pm.Brand, pm.Expiry, pm.CardholderName));
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .Produces(422)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IRepository<PaymentMethod> repo)
        => throw new System.NotImplementedException();

    private static string GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue("sub")
        ?? user.Identity?.Name
        ?? throw new System.UnauthorizedAccessException();
}

public record CreatePaymentMethodRequest(string Number, string Expiry, string SecurityCode, string? Name, string? CountryCode);
public record CreatePaymentMethodResponse(int PaymentMethodId, string? LastFour, string? Brand, string? Expiry, string? Name);
