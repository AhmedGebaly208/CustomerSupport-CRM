using CustomerSupportCRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute or content-root-relative directory that holds attachment bytes.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";

    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Allow-list, not a block-list: a block-list of "dangerous" extensions is
    /// always incomplete.</summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".rtf",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".zip", ".rar", ".7z",
        ".eml", ".msg", ".mp3", ".mp4", ".wav", ".ogg"
    ];
}

public sealed class LocalFileStorage(
    IOptions<FileStorageOptions> options,
    ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<string> SaveAsync(Stream content, string fileName, string subFolder, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension))
            throw new InvalidOperationException($"File type '{extension}' is not allowed.");

        // Never reuse the client-supplied name on disk: it is attacker-controlled and a
        // source of path traversal and collisions. The original name lives on the DB row.
        var safeName = $"{Guid.NewGuid():N}{extension}";
        var relativeFolder = SanitizeFolder(subFolder);
        var relativePath = Path.Combine(relativeFolder, safeName).Replace('\\', '/');

        var absoluteFolder = Path.Combine(RootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var absolutePath = Path.Combine(absoluteFolder, safeName);

        await using (var target = File.Create(absolutePath))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        logger.LogInformation("Stored attachment {RelativePath} ({OriginalName})", relativePath, fileName);
        return relativePath;
    }

    public Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveWithinRoot(relativePath);

        if (absolutePath is null || !File.Exists(absolutePath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(absolutePath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveWithinRoot(relativePath);

        if (absolutePath is not null && File.Exists(absolutePath))
            File.Delete(absolutePath);

        return Task.CompletedTask;
    }

    private string RootPath => Path.IsPathRooted(_options.RootPath)
        ? _options.RootPath
        : Path.Combine(AppContext.BaseDirectory, _options.RootPath);

    /// <summary>Resolves a stored relative path and refuses anything that escapes the
    /// storage root, so a tampered DB value cannot read arbitrary files.</summary>
    private string? ResolveWithinRoot(string relativePath)
    {
        var root = Path.GetFullPath(RootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));

        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static string SanitizeFolder(string subFolder)
    {
        var segments = subFolder
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "." && s != "..")
            .Select(s => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c))))
            .Where(s => s.Length > 0);

        return Path.Combine(segments.ToArray());
    }
}
