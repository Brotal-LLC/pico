using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Tests.Unit;

/// <summary>
/// Tests for the User domain entity.
/// These are intentionally pure-logic tests with no IO or dependencies.
/// Each test covers one observable behavior: creation, role assignment, password handling.
/// </summary>
public class UserEntityTests
{
    [Fact]
    public void Create_WithValidArgs_ReturnsUserWithId()
    {
        // Arrange
        var email = "alice@example.com";
        var name = "Alice";
        var passwordHash = "argon2id$hash$placeholder";

        // Act
        var user = User.Create(email, name, passwordHash, UserRole.Customer);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(email, user.Email);
        Assert.Equal(name, user.Name);
        Assert.Equal(passwordHash, user.PasswordHash);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.True(user.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.True(user.CreatedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEmail_Throws(string? email)
    {
        Assert.Throws<ArgumentException>(() =>
            User.Create(email!, "Alice", "hash", UserRole.Customer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankPasswordHash_Throws(string? hash)
    {
        Assert.Throws<ArgumentException>(() =>
            User.Create("alice@example.com", "Alice", hash!, UserRole.Customer));
    }

    [Fact]
    public void Create_WithAdminRole_PreservesAdminRole()
    {
        var user = User.Create("admin@example.com", "Admin", "hash", UserRole.Admin);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.True(user.IsAdmin());
        Assert.False(user.IsCustomer());
    }

    [Fact]
    public void ChangeName_UpdatesName()
    {
        var user = User.Create("alice@example.com", "Alice", "hash", UserRole.Customer);
        user.ChangeName("Alicia");
        Assert.Equal("Alicia", user.Name);
    }

    [Fact]
    public void UpdatePasswordHash_ReplacesHash()
    {
        var user = User.Create("alice@example.com", "Alice", "old-hash", UserRole.Customer);
        user.UpdatePasswordHash("new-hash");
        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void IsCustomer_ReturnsTrueForCustomerRole()
    {
        var user = User.Create("alice@example.com", "Alice", "hash", UserRole.Customer);
        Assert.True(user.IsCustomer());
        Assert.False(user.IsAdmin());
    }
}