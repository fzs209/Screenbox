using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Playback;
using Windows.Media.Core;
using Windows.Storage;
using IMediaSource = Screenbox.Core.MediaSources.IMediaSource;

namespace Screenbox.Core.Services;

public sealed partial class PlayerService : IPlayerService
{
    private readonly ILogger<PlayerService> _logger;

    public PlayerService(ILogger<PlayerService> logger)
    {
        _logger = logger;
    }

    public IMediaPlayer Initialize(string[] swapChainOptions)
    {
        // The swap-chain options belonged to the previous backend's video
        // surface. The parameter is kept so callers stay unchanged; the
        // Media Foundation player needs no such options.
        _ = swapChainOptions;
        return new MfMediaPlayer();
    }

    public IPlaybackItem CreatePlaybackItem(IMediaPlayer player, IMediaSource source, params string[] options)
    {
        if (player is not MfMediaPlayer)
            throw new NotSupportedException("Only MfMediaPlayer is supported");

        // Per-file playback options were command-line options for the
        // previous backend. Media Foundation has no equivalent, so the
        // stored options are ignored here. See MF-NOTES.md.
        _ = options;

        MediaSource mediaSource = CreateMediaSource(source);
        return new MfPlaybackItem(source, mediaSource);
    }

    public void DisposePlayer(IMediaPlayer player)
    {
        if (player is MfMediaPlayer mfPlayer)
        {
            mfPlayer.Close();
        }
    }

    private MediaSource CreateMediaSource(IMediaSource source)
    {
        return source switch
        {
            IFileMediaSource fileSource => CreateMediaSource(fileSource),
            IUriMediaSource uriSource => MediaSource.CreateFromUri(uriSource.Uri),
            _ => throw new ArgumentOutOfRangeException(nameof(source), $"Unsupported media source type: {source.GetType().Name}")
        };
    }

    private MediaSource CreateMediaSource(IFileMediaSource fileSource)
    {
        StorageFile? file = fileSource.StorageFile ?? TryResolveStorageFile(fileSource);
        if (file != null)
        {
            return MediaSource.CreateFromStorageFile(file);
        }

        if (Uri.TryCreate(fileSource.Path, UriKind.Absolute, out Uri? uri))
        {
            return MediaSource.CreateFromUri(uri);
        }

        throw new ArgumentOutOfRangeException(nameof(fileSource), $"Unsupported file path: {fileSource.Path}");
    }

    private StorageFile? TryResolveStorageFile(IFileMediaSource fileSource)
    {
        // Playback items are created synchronously, but resolving a file
        // from its access token or path is asynchronous. Resolve on a
        // background thread so the wait below cannot deadlock the caller,
        // and fall back to the file path if resolution does not finish.
        try
        {
            Task<StorageFile?> resolveTask = Task.Run(() => fileSource.GetStorageFileAsync());
            if (resolveTask.Wait(TimeSpan.FromSeconds(5)))
            {
                return resolveTask.Result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve a storage file for playback.");
        }

        return null;
    }
}
