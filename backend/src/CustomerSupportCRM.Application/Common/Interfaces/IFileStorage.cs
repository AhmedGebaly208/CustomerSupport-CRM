namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Attachment byte storage. The local-disk implementation ships now; the
/// `integrations` story can swap in blob storage without touching Application code.</summary>
public interface IFileStorage
{
    /// <returns>Path relative to the storage root, to persist on the Attachment row.</returns>
    Task<string> SaveAsync(Stream content, string fileName, string subFolder, CancellationToken cancellationToken = default);

    Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
