using Xunit;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_is_verifiable_and_salted()
    {
        var first = PasswordHasher.Hash("A secure test password!");
        var second = PasswordHasher.Hash("A secure test password!");
        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("A secure test password!", first));
        Assert.False(PasswordHasher.Verify("wrong password", first));
    }

    [Fact]
    public void Login_accepts_the_password_that_created_the_hash()
    {
        var hash = PasswordHasher.Hash("A secure test password!");

        Assert.True(PasswordHasher.Verify("A secure test password!", hash));
    }

    [Fact]
    public void Login_rejects_an_incorrect_password()
    {
        var hash = PasswordHasher.Hash("A secure test password!");

        Assert.False(PasswordHasher.Verify("not-the-password", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-pbkdf2-hash")]
    public void Empty_or_malformed_hashes_never_authenticate(string? stored) => Assert.False(PasswordHasher.Verify("anything", stored));
}
