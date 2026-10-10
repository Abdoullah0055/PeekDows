using System;
using System.IO;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class SoundFileValidatorTests
{
    [Theory]
    [InlineData("sound.mp3", true)]
    [InlineData("sound.wav", true)]
    [InlineData("sound.MP3", true)]
    [InlineData("sound.WAV", true)]
    [InlineData("sound.ogg", false)]
    [InlineData("sound", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasSupportedExtension_ReturnsExpected(string? fileName, bool expected)
    {
        Assert.Equal(expected, SoundFileValidator.HasSupportedExtension(fileName));
    }

    [Fact]
    public void Sanitize_AbsolutePath_ReturnsFileNameOnly()
    {
        var result = SoundFileValidator.SanitizeFileName(@"C:\Music\chime.mp3");

        Assert.Equal("chime.mp3", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_Empty_ReturnsEmpty(string? fileName)
    {
        Assert.Equal(string.Empty, SoundFileValidator.SanitizeFileName(fileName));
    }

    [Fact]
    public void Sanitize_InvalidChars_AreReplaced()
    {
        var invalid = Path.GetInvalidFileNameChars();
        if (invalid.Length == 0)
            return;
        var fileName = "a" + invalid[0] + "b.mp3";

        var result = SoundFileValidator.SanitizeFileName(fileName);

        Assert.Equal("a-b.mp3", result);
        Assert.DoesNotContain(invalid[0].ToString(), result);
    }

    [Fact]
    public void Sanitize_LongName_TruncatesTo80KeepingExtension()
    {
        var longName = new string('a', 100) + ".mp3";

        var result = SoundFileValidator.SanitizeFileName(longName);

        Assert.True(result.Length <= 80);
        Assert.Equal(".mp3", Path.GetExtension(result));
    }

    [Fact]
    public void Sanitize_UnsupportedExtension_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SoundFileValidator.SanitizeFileName("sound.ogg"));
    }

    [Fact]
    public void Deduplicate_MissingDir_ReturnsFileNameAsIs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Assert.Equal("sound.mp3", SoundFileValidator.Deduplicate(dir, "sound.mp3"));
    }

    [Fact]
    public void Deduplicate_ExistingFile_ReturnsSuffixedName()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sound.mp3"), "x");

            Assert.Equal("sound-2.mp3", SoundFileValidator.Deduplicate(dir, "sound.mp3"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Deduplicate_TwoExistingFiles_ReturnsThirdName()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sound.mp3"), "x");
            File.WriteAllText(Path.Combine(dir, "sound-2.mp3"), "x");

            Assert.Equal("sound-3.mp3", SoundFileValidator.Deduplicate(dir, "sound.mp3"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ComputeAutoOffDeadline_Zero_ReturnsNull()
    {
        Assert.Null(ArrangeSoundPlanning.ComputeAutoOffDeadline(DateTime.UtcNow, 0));
    }

    [Fact]
    public void ComputeAutoOffDeadline_Negative_ReturnsNull()
    {
        Assert.Null(ArrangeSoundPlanning.ComputeAutoOffDeadline(DateTime.UtcNow, -3));
    }

    [Fact]
    public void ComputeAutoOffDeadline_NinetyMinutes_ReturnsNowPlusNinetyMinutes()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddMinutes(90), ArrangeSoundPlanning.ComputeAutoOffDeadline(now, 90));
    }
}
