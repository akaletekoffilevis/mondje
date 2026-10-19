using Microsoft.Extensions.Configuration;

namespace Mondje.Infrastructure.Imports;

/// <summary>Shared location where uploaded import files are kept on disk (needed for resume).</summary>
public static class ImportStorage
{
    public static string GetFolder(IConfiguration configuration)
    {
        var folder = configuration["Storage:ImportFolder"]
            ?? Environment.GetEnvironmentVariable("IMPORT_FOLDER")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string GetPath(IConfiguration configuration, string storedFileName) =>
        Path.Combine(GetFolder(configuration), storedFileName);
}