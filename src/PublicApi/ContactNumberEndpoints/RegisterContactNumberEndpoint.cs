using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; the stored value is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(http.User);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "A phone number is required." });
                }

                var registered = await service.RegisterAsync(buyerId, request.PhoneNumber, ct);
                if (registered is null)
                {
                    return Results.BadRequest(new { message = "The number is not a usable SMS destination." });
                }

                var response = new RegisterContactNumberResponse
                {
                    ContactNumberId = registered.ContactNumberId,
                    PhoneNumber = registered.PhoneNumber,
                    CountryCode = registered.CountryCode,
                    RegisteredAtUtc = registered.RegisteredAtUtc
                };

                return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
