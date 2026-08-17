using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. A number the provider
/// does not consider a usable destination is rejected here (400); the provider's canonical form is stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                IContactNumberService contactNumberService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var result = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, cancellationToken);
                if (!result.Succeeded || result.ContactNumber is null)
                {
                    return Results.BadRequest(new { error = result.Error });
                }

                var contactNumber = result.ContactNumber;
                var response = new RegisterContactNumberResponse
                {
                    ContactNumberId = contactNumber.Id,
                    PhoneNumber = contactNumber.E164Number,
                    RegisteredAt = contactNumber.RegisteredAt
                };
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
