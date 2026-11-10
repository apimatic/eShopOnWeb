using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>A class a shopper can attend. Times are ISO-8601 date-times with the studio's UTC offset.</summary>
public class StudioClassDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? TrainerName { get; set; }
    public int? SpotsLeft { get; set; }
}

/// <summary>A time a one-to-one session can start.</summary>
public class StudioSlotDto
{
    public int SessionTypeId { get; set; }
    public string? SessionTypeName { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

public class StudioBookingDto
{
    /// <summary>"class" or "appointment".</summary>
    public string Kind { get; set; } = "";
    public long BookingId { get; set; }
    public int? ClassId { get; set; }
    public int? SessionTypeId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int? LocationId { get; set; }
    public long? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public string ClientId { get; set; } = "";
    /// <summary>True when the same booking already existed (for example the app repeated its request).</summary>
    public bool AlreadyBooked { get; set; }
}

public class StudioCancellationDto
{
    public string Kind { get; set; } = "";
    public long BookingId { get; set; }
    public bool Cancelled { get; set; } = true;
}

public class StudioTrainerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
    public bool ClassTeacher { get; set; }
    public bool AppointmentInstructor { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// The details the studio asks of every new client, and the home location. Only needed with a shopper's first booking.
/// A few alternative property names are accepted for the same field.
/// </summary>
public class StudioClientDetails
{
    public int? HomeLocationId { get; set; }
    public int? LocationId { get; set; }

    public string? StreetAddress { get; set; }
    public string? Address { get; set; }
    public string? AddressLine1 { get; set; }

    public string? City { get; set; }
    public string? State { get; set; }

    public string? PostalCode { get; set; }
    public string? ZipCode { get; set; }
    public string? Zip { get; set; }

    public string? DateOfBirth { get; set; }
    public string? BirthDate { get; set; }
    public string? Dob { get; set; }

    public string? MobilePhone { get; set; }
    public string? MobileNumber { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }

    public string? ReferredBy { get; set; }
    public string? HowDidYouHear { get; set; }
    public string? ReferralSource { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    public int? ResolvedHomeLocationId => HomeLocationId ?? LocationId;
    public string? ResolvedStreet => First(StreetAddress, Address, AddressLine1);
    public string? ResolvedCity => First(City);
    public string? ResolvedState => First(State);
    public string? ResolvedPostalCode => First(PostalCode, ZipCode, Zip);
    public string? ResolvedDateOfBirthText => First(DateOfBirth, BirthDate, Dob);
    public string? ResolvedMobile => First(MobilePhone, MobileNumber, Mobile, Phone);
    public string? ResolvedReferral => First(ReferredBy, HowDidYouHear, ReferralSource);

    private static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Whether any new-client detail was supplied at all.</summary>
    public bool HasAny =>
        ResolvedHomeLocationId != null || ResolvedStreet != null || ResolvedCity != null || ResolvedState != null ||
        ResolvedPostalCode != null || ResolvedDateOfBirthText != null || ResolvedMobile != null || ResolvedReferral != null;

    /// <summary>Validates the details for a new client. Returns the problems found (empty when valid) and the parsed date of birth.</summary>
    public List<string> Validate(DateOnly today, out DateOnly dateOfBirth)
    {
        var problems = new List<string>();
        dateOfBirth = default;

        if (ResolvedHomeLocationId is not > 0) problems.Add("homeLocationId");
        if (ResolvedStreet == null) problems.Add("streetAddress");
        if (ResolvedCity == null) problems.Add("city");
        if (ResolvedState == null) problems.Add("state");
        if (ResolvedPostalCode == null) problems.Add("postalCode");
        if (ResolvedMobile == null) problems.Add("mobilePhone");
        if (ResolvedReferral == null) problems.Add("referredBy");

        var dobText = ResolvedDateOfBirthText;
        if (dobText == null)
        {
            problems.Add("dateOfBirth");
        }
        else if (!TryParseDate(dobText, out dateOfBirth))
        {
            problems.Add("dateOfBirth (use yyyy-MM-dd)");
        }
        else if (dateOfBirth >= today || dateOfBirth < today.AddYears(-120))
        {
            problems.Add("dateOfBirth (not a plausible date of birth)");
        }

        if (ResolvedMobile != null && ResolvedMobile.Count(char.IsDigit) < 7)
            problems.Add("mobilePhone (not a valid phone number)");

        return problems;
    }

    public static bool TryParseDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        // A full ISO date-time: the calendar date as written.
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            date = DateOnly.FromDateTime(dto.DateTime);
            return true;
        }
        return false;
    }

    private static readonly Regex NonName = new("[^\\p{L}\\p{N}]+", RegexOptions.Compiled);

    /// <summary>Names for a new client: given explicitly, otherwise derived from the e-mail address of the eShop identity.</summary>
    public (string First, string Last) NamesFor(string email)
    {
        var first = string.IsNullOrWhiteSpace(FirstName) ? null : FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(LastName) ? null : LastName.Trim();
        if (first != null && last != null) return (first, last);

        var local = email.Split('@')[0];
        var parts = NonName.Split(local).Where(p => p.Length > 0).Select(Capitalize).ToList();
        if (parts.Count == 0) parts.Add("Shopper");
        first ??= parts[0];
        last ??= parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : "Shopper";
        return (first, last);
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
}

public class BookClassRequest : StudioClientDetails
{
    public int ClassId { get; set; }
}

public class BookAppointmentRequest : StudioClientDetails
{
    public int SessionTypeId { get; set; }

    /// <summary>ISO-8601 start. Without an offset it is read as studio local time.</summary>
    public string? StartTime { get; set; }
    public string? StartDateTime { get; set; }

    /// <summary>Optionally the trainer to book with; otherwise any trainer who is free is used.</summary>
    public long? StaffId { get; set; }
    public long? TrainerId { get; set; }
    public int? SessionLocationId { get; set; }
}

public class AdminBookClassRequest : StudioClientDetails
{
    /// <summary>The shopper: the eShop user name or e-mail address.</summary>
    public string? Shopper { get; set; }
    public string? ShopperEmail { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public int ClassId { get; set; }

    public string? ResolvedShopper =>
        new[] { Shopper, ShopperEmail, Email, UserName }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
