using PeekDows.App.Settings;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsWebViewOriginTests
{
    [Theory]
    [InlineData("https://app.local/index.html")]
    [InlineData("https://APP.LOCAL/index.html#layout")]
    public void AllowsTheLocalSettingsDocument(string address)
        => Assert.True(SettingsHostForm.IsSettingsDocument(address));

    [Theory]
    [InlineData("http://app.local/index.html")]
    [InlineData("https://example.com/index.html")]
    [InlineData("https://app.local.example.com/index.html")]
    [InlineData("https://app.local:444/index.html")]
    [InlineData("https://user@app.local/index.html")]
    [InlineData("https://app.local/")]
    [InlineData("https://app.local/app.js")]
    [InlineData("https://app.local/index.html?mode=layout")]
    [InlineData("not a URL")]
    [InlineData(null)]
    public void BlocksOtherDocuments(string? address)
        => Assert.False(SettingsHostForm.IsSettingsDocument(address));
}
