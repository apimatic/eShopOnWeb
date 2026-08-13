using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using Microsoft.Extensions.DependencyInjection;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider
/// and stored in the provider's own canonical E.164 form; a number the provider does not consider a
/// usable destination is rejected here, not at send time.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext http)
    {
        var ownerId = CallerIdentity.GetOwnerId(http.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { error = "A phone number is required." });

        var sms = http.RequestServices.GetRequiredService<ISmsProvider>();
        var repository = http.RequestServices.GetRequiredService<IRepository<ContactNumber>>();

        var validation = await sms.ValidateNumberAsync(request.PhoneNumber, http.RequestAborted);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
            return Results.BadRequest(new { error = validation.Reason ?? "The number is not a usable destination." });

        var canonical = validation.CanonicalNumber!;

        // Idempotent registration: if this shopper already has this canonical number, return it.
        var existing = await repository.FirstOrDefaultAsync(
            new ContactNumberByValueForOwnerSpecification(ownerId, canonical), http.RequestAborted);
        if (existing is not null)
            return Results.Ok(new RegisterContactNumberResponse { ContactNumberId = existing.Id, PhoneNumber = existing.PhoneNumber });

        var contactNumber = new ContactNumber(ownerId, canonical);
        await repository.AddAsync(contactNumber, http.RequestAborted);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
