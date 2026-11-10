using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>A class shoppers can attend. Times carry the studio's UTC offset.</summary>
public class StudioClassDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Trainer { get; set; }
    public int? Capacity { get; set; }
    public int? Booked { get; set; }
}

/// <summary>A time a one-to-one session can start. Times carry the studio's UTC offset.</summary>
public class StudioSlotDto
{
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioBookingDto
{
    public long BookingId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public long? TrainerId { get; set; }
    public int? LocationId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? HomePhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

/// <summary>Who is calling, taken from the caller's JWT and eShop identity.</summary>
public sealed record StudioCaller(string UserName, string Email);

/// <summary>
/// What the studio asks of every new client. Only needed on a shopper's first booking.
/// Values arrive as the caller typed them and are validated by the service.
/// </summary>
public sealed class StudioClientProfile
{
    public int? HomeLocationId { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? BirthDate { get; set; }
    public string? MobilePhone { get; set; }
    public string? ReferredBy { get; set; }
}
