using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.MediaSources;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using IMediaSource = Screenbox.Core.MediaSources.IMediaSource;

namespace Screenbox.Core.Playback;

/// <summary>
/// Media Foundation implementation of <see cref="IPlaybackItem"/>.
/// Wraps a system <see cref="MediaPlaybackItem"/> created from a
/// <see cref="MediaSource"/>.
/// </summary>
public sealed partial class MfPlaybackItem : IPlaybackItem
{
    internal MediaPlaybackItem SystemPlaybackItem { get; }

    internal MediaSource SystemMediaSource { get; }

    public IMediaSource Source { get; }

    public TimeSpan? Duration => SystemMediaSource.Duration;

    public MfAudioTrackList AudioTracks { get; }

    public MfVideoTrackList VideoTracks { get; }

    public MfSubtitleTrackList SubtitleTracks { get; }

    public MfChapterList Chapters { get; }

    ISingleSelectMediaTrackList<AudioTrack> IPlaybackItem.AudioTracks => AudioTracks;

    ISingleSelectMediaTrackList<VideoTrack> IPlaybackItem.VideoTracks => VideoTracks;

    ISubtitleTrackList IPlaybackItem.SubtitleTracks => SubtitleTracks;

    IReadOnlyList<ChapterCue> IPlaybackItem.Chapters => Chapters;

    private readonly List<IRandomAccessStream> _externalSubtitleStreams = new();
    private bool _isDisposed;

    public MfPlaybackItem(IMediaSource source, MediaSource mediaSource)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SystemMediaSource = mediaSource ?? throw new ArgumentNullException(nameof(mediaSource));
        SystemPlaybackItem = new MediaPlaybackItem(mediaSource);
        AudioTracks = new MfAudioTrackList(SystemPlaybackItem);
        VideoTracks = new MfVideoTrackList(SystemPlaybackItem);
        SubtitleTracks = new MfSubtitleTrackList(SystemPlaybackItem, this);
        Chapters = new MfChapterList(this);
    }

    internal void RefreshTracks()
    {
        AudioTracks.Refresh();
        VideoTracks.Refresh();
        SubtitleTracks.Refresh();
    }

    internal void KeepExternalSubtitleStream(IRandomAccessStream stream)
    {
        _externalSubtitleStreams.Add(stream);
    }

    public async Task WaitForParsedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return;
        }

        if (SystemMediaSource.State == MediaSourceState.Opened)
        {
            RefreshTracks();
            return;
        }

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(MediaSource sender, MediaSourceStateChangedEventArgs e)
        {
            if (e.NewState is MediaSourceState.Opened or MediaSourceState.Failed)
            {
                tcs.TrySetResult(true);
            }
        }

        SystemMediaSource.StateChanged += OnStateChanged;
        try
        {
            try
            {
                // Start opening the source so tracks and metadata become
                // available even before playback begins.
                _ = SystemMediaSource.OpenAsync();
            }
            catch (Exception)
            {
                // Opening also starts lazily when the item is set as the
                // player source; the wait below still applies.
            }

            await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Timed out or canceled; fall through and refresh with
            // whatever tracks are currently available.
        }
        finally
        {
            SystemMediaSource.StateChanged -= OnStateChanged;
        }

        RefreshTracks();
    }

    public Task<IReadOnlyList<ExpandedMediaItem>> ExpandSubItemsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Media Foundation does not expand a media source into sub-items the
        // way the previous backend did (for example playlist entries inside
        // a container). Expandable sources are handled by their own
        // IExpandableMediaSource implementation instead. See MF-NOTES.md.
        return Task.FromResult<IReadOnlyList<ExpandedMediaItem>>(Array.Empty<ExpandedMediaItem>());
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        AudioTracks.Detach();
        VideoTracks.Detach();
        SubtitleTracks.Detach();

        SystemMediaSource.Dispose();
        foreach (IRandomAccessStream stream in _externalSubtitleStreams)
        {
            stream.Dispose();
        }

        _externalSubtitleStreams.Clear();

        GC.SuppressFinalize(this);
    }
}
