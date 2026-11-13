using LabelGuard.Core.Config;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Tests.Config;

public class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"labelguard-cfg-{Guid.NewGuid():N}");

    private string ConfigPath => Path.Combine(_dir, LabelGuardConfig.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var config = LabelGuardConfig.Load(ConfigPath);

        Assert.Equal("LabelGuard - NAO USAR", config.OutputPrinter);
        Assert.Equal(9100, config.Port);
        Assert.Equal(IdExtractor.DefaultPattern, config.IdPattern);
        Assert.True(config.PrinterReprintOff);
        Assert.Null(config.SupervisorPasswordHash);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var config = new LabelGuardConfig
        {
            OutputPrinter = "Outra",
            Port = 9200,
            AllowMultipleCopies = true,
            SaveJobsTo = @"C:\temp\recebidos",
            SupervisorPasswordHash = SupervisorPassword.Hash("1234"),
        };

        config.Save(ConfigPath);
        var loaded = LabelGuardConfig.Load(ConfigPath);

        Assert.Equal("Outra", loaded.OutputPrinter);
        Assert.Equal(9200, loaded.Port);
        Assert.True(loaded.AllowMultipleCopies);
        Assert.False(loaded.PrintWithoutId);
        Assert.Equal(@"C:\temp\recebidos", loaded.SaveJobsTo);
        Assert.True(SupervisorPassword.Verify("1234", loaded.SupervisorPasswordHash));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Load_AcceptsCommentsAndPartialFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, """
            {
              // fila ligada na USB
              "outputPrinter": "LABEL USB",
              "printerReprintOff": false,
            }
            """);

        var config = LabelGuardConfig.Load(ConfigPath);

        Assert.Equal("LABEL USB", config.OutputPrinter);
        Assert.False(config.PrinterReprintOff);
        Assert.Equal(9100, config.Port);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "{ isto nao e json");

        Assert.Throws<InvalidDataException>(() => LabelGuardConfig.Load(ConfigPath));
    }

    [Fact]
    public void DatabasePath_DefaultsNextToConfig()
    {
        Assert.Equal(Path.Combine(_dir, "historico.db"), new LabelGuardConfig().ResolveDatabasePath(ConfigPath));
        Assert.Equal(
            Path.Combine(_dir, "dados", "h.db"),
            new LabelGuardConfig { DatabasePath = @"dados\h.db" }.ResolveDatabasePath(ConfigPath));
    }

    [Fact]
    public void Password_VerifiesOnlyTheRightOne()
    {
        var hash = SupervisorPassword.Hash("senha-forte");

        Assert.True(SupervisorPassword.Verify("senha-forte", hash));
        Assert.False(SupervisorPassword.Verify("senha-errada", hash));
        Assert.DoesNotContain("senha-forte", hash);
        Assert.NotEqual(hash, SupervisorPassword.Hash("senha-forte"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-qualquer")]
    [InlineData("pbkdf2-sha256$abc$xx$yy")]
    [InlineData("pbkdf2-sha256$1000$***$***")]
    public void Password_InvalidHash_IsRejected(string? stored)
    {
        Assert.False(SupervisorPassword.Verify("1234", stored));
    }

    [Fact]
    public void Password_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => SupervisorPassword.Hash("123"));
    }
}
