using System;
using System.IO;
using System.Threading.Tasks;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class CodexAuthStoreTests
{
    [Fact]
    public async Task SaveAsync_EncryptsTokensInJsonAndLoadAsync_DecryptsTokens()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "TokenDock.Tests", Guid.NewGuid().ToString("N"), "auth.json");
        var store = new CodexAuthStore(filePath);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        await store.SaveAsync(new CodexAuthTokens(
            AccessToken: "access-token",
            ChatGptAccountId: "account-id",
            RefreshToken: "refresh-token",
            ExpiresAt: expiresAt));

        var json = await File.ReadAllTextAsync(filePath);
        Assert.DoesNotContain("access-token", json);
        Assert.DoesNotContain("account-id", json);
        Assert.DoesNotContain("refresh-token", json);

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("access-token", loaded.AccessToken);
        Assert.Equal("account-id", loaded.ChatGptAccountId);
        Assert.Equal("refresh-token", loaded.RefreshToken);
        Assert.Equal(expiresAt, loaded.ExpiresAt);
    }

}
