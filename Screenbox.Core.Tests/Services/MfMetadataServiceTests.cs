using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Media.Core;
using AudioTrack = Screenbox.Core.Playback.AudioTrack;
using IMediaSource = Screenbox.Core.MediaSources.IMediaSource;
using VideoTrack = Screenbox.Core.Playback.VideoTrack;

namespace Screenbox.Core.Tests.Services;

public partial class MfMetadataServiceTests
{
    private sealed partial class DummyPlaybackItem : IPlaybackItem
    {
        public IMediaSource Source { get; init; } = null!;
        public TimeSpan? Duration => null;
        public ISingleSelectMediaTrackList<AudioTrack> AudioTracks => throw new NotImplementedException();
        public ISingleSelectMediaTrackList<VideoTrack> VideoTracks => throw new NotImplementedException();
        public ISubtitleTrackList SubtitleTracks => throw new NotImplementedException();
        public IReadOnlyList<ChapterCue> Chapters => Array.Empty<ChapterCue>();
        public Task WaitForParsedAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ExpandedMediaItem>> ExpandSubItemsAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExpandedMediaItem>>(Array.Empty<ExpandedMediaItem>());
        public void Dispose() { }
    }

    [Test]
    public async Task GetMediaInfoAsync_WithNullArgument_ShouldThrowArgumentNullException()
    {
        var service = new MfMetadataService(NullLogger<MfMetadataService>.Instance);

        await Assert.That(async () => await service.GetMediaInfoAsync((IPlaybackItem)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetMediaInfoAsync_WithUnsupportedPlaybackItem_ThrowsNotSupportedException()
    {
        var service = new MfMetadataService(NullLogger<MfMetadataService>.Instance);
        var dummyItem = new DummyPlaybackItem();

        await Assert.That(async () => await service.GetMediaInfoAsync(dummyItem)).Throws<NotSupportedException>();
    }
}
