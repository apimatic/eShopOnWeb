using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here, and the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(request, http, service, http.RequestAborted))
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request, HttpContext http, IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        try
        {
            var contact = await service.RegisterContactNumberAsync(buyerId, request.Number, ct);
            return Results.Created($"api/contact-numbers/{contact.Id}", new RegisterContactNumberResponse
            {
                ContactNumberId = contact.Id,
                Number = contact.E164Number
            });
        }
        catch (OrderValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
