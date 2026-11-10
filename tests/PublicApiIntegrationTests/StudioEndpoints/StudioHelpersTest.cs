using System;
using Microsoft.eShopWeb.PublicApi.Studio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

[TestClass]
public class StudioHelpersTest
{
    private static readonly TimeZoneInfo Pacific = StudioTime.FindZone("America/Los_Angeles")!;

    [TestMethod]
    public void AtStudio_UsesTheStudioOffsetOfThatDay()
    {
        Assert.AreEqual(TimeSpan.FromHours(-8), StudioTime.AtStudio(new DateTime(2026, 11, 12, 9, 0, 0), Pacific).Offset);
        Assert.AreEqual(TimeSpan.FromHours(-7), StudioTime.AtStudio(new DateTime(2026, 7, 12, 9, 0, 0), Pacific).Offset);
    }

    [TestMethod]
    public void FromMindbody_KeepsTheWallClockWhateverOffsetTheSdkAttached()
    {
        var readByTheSdk = new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.FromHours(5));
        var result = StudioTime.FromMindbody(readByTheSdk, Pacific);
        Assert.AreEqual("2026-11-12T07:00:00-08:00", result.ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    [TestMethod]
    public void BodyValue_SerialisesTheStudioWallClockDigits()
    {
        var value = StudioTime.BodyValue(new DateTime(2026, 11, 12, 9, 30, 0));
        Assert.AreEqual(TimeSpan.Zero, value.Offset);
        Assert.AreEqual(new DateTime(2026, 11, 12, 9, 30, 0), value.DateTime);
    }

    [TestMethod]
    public void NewClientDetails_AreAllRequired()
    {
        var problems = new StudioClientDetails().Validate(new DateOnly(2026, 11, 10), out _);
        Assert.AreEqual(8, problems.Count);
    }

    [TestMethod]
    public void NewClientDetails_AcceptAlternativeNames()
    {
        var details = new StudioClientDetails
        {
            LocationId = 1, Address = "1 Main St", City = "Austin", State = "TX", Zip = "78701",
            Dob = "1990-05-05", Mobile = "512-555-0100", HowDidYouHear = "Google",
        };
        Assert.AreEqual(0, details.Validate(new DateOnly(2026, 11, 10), out var dob).Count);
        Assert.AreEqual(new DateOnly(1990, 5, 5), dob);
    }

    [TestMethod]
    public void NamesAreDerivedFromTheEmailWhenNotGiven()
    {
        var (first, last) = new StudioClientDetails().NamesFor("jane.doe@example.com");
        Assert.AreEqual("Jane", first);
        Assert.AreEqual("Doe", last);
    }
}
