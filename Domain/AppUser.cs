using Base.Domain;

namespace Domain;

public class AppUser : BaseEntityWithConcurrency
{
    public const string DefaultLocale = "et-EE";
    public const string DefaultTimeZone = "Europe/Tallinn";

    public string Email { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string Locale { get; set; } = DefaultLocale;
    public string TimeZone { get; set; } = DefaultTimeZone;

    public static AppUser FromIdentity(Guid id, string? email, string? username, string? fullName, string? locale, string? timeZone)
    {
        return new AppUser
        {
            Id = id,
            Email = Normalize(email),
            Username = Normalize(username),
            FullName = Normalize(fullName),
            Locale = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim()
        };
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
