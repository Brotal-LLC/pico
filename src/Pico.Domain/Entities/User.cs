using Pico.Domain.Enums;

namespace Pico.Domain.Entities;

/// <summary>
/// Customer or administrator account. Authentication and authorization boundary.
/// </summary>
public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core constructor
    private User() { }

    /// <summary>
    /// Factory: create a new user with validated inputs.
    /// Email and passwordHash are required and non-blank.
    /// </summary>
    public static User Create(string email, string name, string passwordHash, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Name = (name ?? string.Empty).Trim(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void ChangeName(string newName)
    {
        Name = (newName ?? string.Empty).Trim();
    }

    public void UpdatePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new ArgumentException("Password hash is required.", nameof(newHash));
        PasswordHash = newHash;
    }

    public bool IsAdmin() => Role == UserRole.Admin;
    public bool IsCustomer() => Role == UserRole.Customer;
}
