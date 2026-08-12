using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. The
/// provider validates and canonicalises it first; an unusable destination is rejected here,
/// and the stored value is the provider's canonical E.164 form, not what the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                [FromBody] RegisterContactNumberRequest request,
                ClaimsPrincipal user,
                IPhoneNumberLookupService lookupService,
                IRepository<ContactNumber> repository,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
                    return Results.BadRequest(new { message = "A phone number is required." });

                var lookup = await lookupService.LookupAsync(request.PhoneNumber, cancellationToken);
                if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
                    return Results.BadRequest(new { message = lookup.Reason ?? "The number is not a usable destination." });

                var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber!);
                await repository.AddAsync(contactNumber, cancellationToken);

                var response = new RegisterContactNumberResponse
                {
                    ContactNumberId = contactNumber.Id,
                    PhoneNumber = contactNumber.PhoneNumber
                };
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
