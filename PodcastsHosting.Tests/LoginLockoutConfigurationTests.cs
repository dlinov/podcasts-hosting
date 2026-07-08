namespace PodcastsHosting.Tests;

public class LoginLockoutConfigurationTests
{
    [Fact]
    public void Program_ConfiguresIdentityLockoutOptions()
    {
        var programSource = File.ReadAllText(Path.Combine("..", "..", "..", "..", "PodcastsHosting", "Program.cs"));

        Assert.Contains("options.Lockout.AllowedForNewUsers = true", programSource);
        Assert.Contains("options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)", programSource);
        Assert.Contains("options.Lockout.MaxFailedAccessAttempts = 5", programSource);
    }

    [Fact]
    public void LoginPost_CountsPasswordFailuresTowardLockout()
    {
        var loginSource = File.ReadAllText(Path.Combine("..", "..", "..", "..", "PodcastsHosting", "Areas", "Identity", "Pages", "Account", "Login.cshtml.cs"));

        Assert.Contains("lockoutOnFailure: true", loginSource);
        Assert.DoesNotContain("lockoutOnFailure: false", loginSource);
    }
}