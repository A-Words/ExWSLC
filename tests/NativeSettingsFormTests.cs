using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;

namespace ExWSLC.Tests;

public sealed class NativeSettingsFormTests
{
    [Theory]
    [InlineData(">-", "\n")]
    [InlineData(">", "\n")]
    [InlineData(">+", "\n")]
    [InlineData("|-", "\n")]
    [InlineData("|", "\r\n")]
    [InlineData("|+", "\r\n")]
    [InlineData(">2-", "\r\n")]
    public void UpdatingBlockScalarPreservesFollowingKeysCommentsAndLineEndings(string style, string newline)
    {
        var suffix = newline + newline + "  # keep CPU" + newline + "  cpuCount: 2" + newline + "credentialStore: wincred" + newline;
        var text = "session:" + newline + "  memorySize: " + style + newline + "    4GB" + suffix;
        var result = NativeSettingsYaml.Update(text, "session.memorySize", "8GB");
        Assert.Equal("session:" + newline + "  memorySize: \"8GB\"" + suffix, result);
        Assert.True(NativeSettingsStore.IsValid(result), result);
        Assert.Equal("8GB", NativeSettingsYaml.Read(result, "session.memorySize"));
        Assert.Equal("2", NativeSettingsYaml.Read(result, "session.cpuCount"));
        Assert.Equal("wincred", NativeSettingsYaml.Read(result, "credentialStore"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Idle")]
    public void ClearingNumberInvalidatesCustomValueAndCanRecover(string key)
    {
        var setting = new NativeSettingViewModel(NativeSettingDefinition.All.Single(definition => definition.Key == key), "4");
        setting.NumericValue = 6;
        setting.NumericValue = null;
        Assert.Null(setting.NumericValue);
        Assert.Equal(string.Empty, setting.Value);
        Assert.False(setting.IsValid);
        setting.UseDefault = true;
        Assert.True(setting.IsValid);
        setting.UseDefault = false;
        Assert.False(setting.IsValid);
        setting.NumericValue = 8;
        Assert.Equal("8", setting.Value);
        Assert.True(setting.IsValid);
    }

    [Theory]
    [InlineData("# comment\nsession:\n  # CPU\n  cpuCount: 2 # retain\nfuture: [a, b]\n")]
    [InlineData("# comment\nsession: {cpuCount: 2, other: yes}\n")]
    [InlineData("session:\n  memorySize: 4GB\n")]
    [InlineData("session: {}\n")]
    [InlineData("session:\n  # empty\ncredentialStore: wincred\n")]
    [InlineData("credentialStore: wincred\n")]
    [InlineData("# empty\n")]
    [InlineData("---\nsession: {}\n...\n")]
    [InlineData("session: {cpuCount: 2}\r\n")]
    public void UpdatingOneSettingProducesValidYamlAndReadsBack(string text)
    {
        var result = NativeSettingsYaml.Update(text, "session.cpuCount", "4");
        Assert.True(NativeSettingsStore.IsValid(result), result);
        Assert.Equal("4", NativeSettingsYaml.Read(result, "session.cpuCount"));
        if (text.Contains("# comment")) Assert.Contains("# comment", result);
        if (text.Contains("# retain")) Assert.Contains("# retain", result);
        if (text.Contains("future: [a, b]")) Assert.Contains("future: [a, b]", result);
    }

    [Fact]
    public void EveryFieldRoundTripsWithoutChangingOtherFields()
    {
        var text = NativeSettingsStore.Template;
        foreach (var definition in NativeSettingDefinition.All)
        {
            text = NativeSettingsYaml.Update(text, definition.Path, "default");
            Assert.True(NativeSettingsStore.IsValid(text), text);
            Assert.Equal("default", NativeSettingsYaml.Read(text, definition.Path));
        }
        text = NativeSettingsYaml.Update(text, "session.storagePath", @"D:\data # user's files");
        Assert.Equal(@"D:\data # user's files", NativeSettingsYaml.Read(text, "session.storagePath"));
        Assert.Contains("# cpuCount: default", text);
    }

    [Theory]
    [InlineData("Cpu", "0", false)]
    [InlineData("Cpu", "1.5", false)]
    [InlineData("Cpu", "4", true)]
    [InlineData("Memory", "4GB", true)]
    [InlineData("Memory", "0MB", false)]
    [InlineData("Disk", "999999999999TB", false)]
    [InlineData("Address", "127.0.0.1", true)]
    [InlineData("Address", "127.1", false)]
    [InlineData("Idle", "30", true)]
    [InlineData("Network", "invalid", false)]
    [InlineData("Dns", "false", true)]
    [InlineData("Host", "none", true)]
    [InlineData("Storage", "relative", false)]
    public void FormValidatesTypedValues(string key, string value, bool valid)
    {
        Assert.Equal(valid, NativeSettingDefinition.All.Single(definition => definition.Key == key).IsValid(value));
    }

    [Fact]
    public void UnrecognizedValuesArePreservedUntilEditedAndDefaultCanRestoreThem()
    {
        var setting = new NativeSettingViewModel(NativeSettingDefinition.All.Single(definition => definition.Key == "Network"), "future-mode");
        Assert.False(setting.IsDirty);
        Assert.True(setting.IsValid);
        setting.SelectedChoice = null;
        Assert.Equal("future-mode", setting.Value);
        setting.UseDefault = true;
        Assert.Equal("default", setting.EffectiveValue);
        Assert.True(setting.IsDirty);
        setting.UseDefault = false;
        Assert.False(setting.IsDirty);
    }
}
