using System.ComponentModel.DataAnnotations;
using Base.Domain;
using Domain;

namespace DTO.DataAccess;

public class AppUserEntity : BaseEntityWithMetaConcurrency
{
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [Required]
    [MaxLength(256)]
    public string Username { get; set; } = default!;

    [Required]
    [MaxLength(256)]
    public string FullName { get; set; } = default!;

    [Required]
    [MaxLength(16)]
    public string Locale { get; set; } = AppUser.DefaultLocale;

    [Required]
    [MaxLength(64)]
    public string TimeZone { get; set; } = AppUser.DefaultTimeZone;
}
