using Microsoft.Extensions.Configuration;
using Mondje.Infrastructure.Imports;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Infrastructure.IntegrationTests.Imports;

/// <summary>
/// The import folder is where uploaded files are kept so an interrupted import can be
/// resumed. These tests pin the precedence (configuration &gt; environment &gt; default)
/// and make sure the folder always exists before the engine writes to it.
/// </summary>
public class ImportStorageTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "mondje-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("IMPORT_FOLDER", null);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IConfiguration Config(string? folder) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ImportFolder"] = folder,
            })
            .Build();

    [Test]
    public void Configured_folder_is_used_and_created()
    {
        var folder = Path.Combine(_root, "configured");

        ImportStorage.GetFolder(Config(folder)).ShouldBe(folder);
        Directory.Exists(folder).ShouldBeTrue();
    }

    [Test]
    public void Environment_variable_is_used_when_configuration_is_absent()
    {
        var folder = Path.Combine(_root, "from-env");
        Environment.SetEnvironmentVariable("IMPORT_FOLDER", folder);

        ImportStorage.GetFolder(Config(null)).ShouldBe(folder);
        Directory.Exists(folder).ShouldBeTrue();
    }

    [Test]
    public void Configuration_takes_precedence_over_the_environment_variable()
    {
        var configured = Path.Combine(_root, "configured");
        Environment.SetEnvironmentVariable("IMPORT_FOLDER", Path.Combine(_root, "from-env"));

        ImportStorage.GetFolder(Config(configured)).ShouldBe(configured);
    }

    [Test]
    public void GetPath_combines_the_folder_and_the_server_generated_file_name()
    {
        var folder = Path.Combine(_root, "files");
        var name = $"{Guid.NewGuid():N}.txt";

        var path = ImportStorage.GetPath(Config(folder), name);

        path.ShouldBe(Path.Combine(folder, name));
        Directory.Exists(folder).ShouldBeTrue();
    }
}
