using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Services;

/// <summary>
/// Playback metadata service for the Media Foundation backend. File-backed
/// items use Windows Storage file properties (the same source the storage
/// metadata service uses); other items report only the duration the system
/// media source knows.
/// </summary>
public sealed class MfMetadataService : IPlaybackMetadataService
{
    private readonly ILogger<MfMetadataService> _logger;

    public MfMetadataService(ILogger<MfMetadataService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MediaInfo?> GetMediaInfoAsync(
        IPlaybackItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is not MfPlaybackItem mfItem)
        {
            throw new NotSupportedException("Only MfPlaybackItem is supported.");
        }

        try
        {
            if (item.Source is IFileMediaSource fileSource
                && await fileSource.GetStorageFileAsync() is { } file)
            {
                return await GetMediaInfoFromFileAsync(file, cancellationToken);
            }

            TimeSpan duration = mfItem.Duration ?? TimeSpan.Zero;
            return new MediaInfo(item.Source.MediaType, string.Empty, 0, duration);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract media metadata from the Media Foundation playback item.");
            return null;
        }
    }

    private static async Task<MediaInfo?> GetMediaInfoFromFileAsync(
        StorageFile file,
        CancellationToken cancellationToken)
    {
        MediaPlaybackType mediaType = FilesHelpers.GetMediaTypeForFile(file);
        if (!file.IsAvailable)
        {
            return new MediaInfo(mediaType);
        }

        BasicProperties basicProperties = await file.GetBasicPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();

        switch (mediaType)
        {
            case MediaPlaybackType.Video:
                VideoProperties videoProperties = await file.Properties.GetVideoPropertiesAsync();
                return new MediaInfo(basicProperties, videoProperties);

            case MediaPlaybackType.Music:
                MusicProperties musicProperties = await file.Properties.GetMusicPropertiesAsync();
                return new MediaInfo(basicProperties, musicProperties);

            default:
                return new MediaInfo(mediaType);
        }
    }
}
