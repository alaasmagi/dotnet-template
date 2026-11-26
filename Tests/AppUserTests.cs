using Domain;

namespace Tests;

public class AppUserTests
{
    [Test]
    public void FromIdentity_TrimsValues()
    {
        var id = Guid.NewGuid();

        var appUser = AppUser.FromIdentity(id, " user@example.com ", " user ", " Full Name ", " en-GB ", " Europe/London ");

        Assert.Multiple(() =>
        {
            Assert.That(appUser.Id, Is.EqualTo(id));
            Assert.That(appUser.Email, Is.EqualTo("user@example.com"));
            Assert.That(appUser.Username, Is.EqualTo("user"));
            Assert.That(appUser.FullName, Is.EqualTo("Full Name"));
            Assert.That(appUser.Locale, Is.EqualTo("en-GB"));
            Assert.That(appUser.TimeZone, Is.EqualTo("Europe/London"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FromIdentity_MissingValues_UseEmptyStringsAndContractFallbacks(string? value)
    {
        var appUser = AppUser.FromIdentity(Guid.NewGuid(), value, value, value, value, value);

        Assert.Multiple(() =>
        {
            Assert.That(appUser.Email, Is.Empty);
            Assert.That(appUser.Username, Is.Empty);
            Assert.That(appUser.FullName, Is.Empty);
            Assert.That(appUser.Locale, Is.EqualTo("et-EE"));
            Assert.That(appUser.TimeZone, Is.EqualTo("Europe/Tallinn"));
        });
    }
}
