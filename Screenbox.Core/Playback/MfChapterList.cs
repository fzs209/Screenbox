using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Screenbox.Core.Helpers;
using Windows.Media.Core;

namespace Screenbox.Core.Playback;

/// <summary>
/// Chapter list for the Media Foundation backend. The system player has no
/// VLC-style chapter descriptions; chapters are surfaced only when the media
/// source exposes chapter cues through a chapter timed-metadata track.
/// </summary>
public sealed partial class MfChapterList : ReadOnlyCollection<ChapterCue>
{
    private readonly List<ChapterCue> _chapters;
    private readonly IPlaybackItem _item;

    internal MfChapterList(IPlaybackItem item) : base(new List<ChapterCue>())
    {
        _item = item;
        _chapters = (List<ChapterCue>)Items;
    }

    public void Load(MfMediaPlayer player)
    {
        if (player.PlaybackItem != _item)
        {
            return;
        }

        if (_item is not MfPlaybackItem mfItem)
        {
            return;
        }

        List<ChapterCue> cues = new();
        foreach (TimedMetadataTrack track in mfItem.SystemPlaybackItem.TimedMetadataTracks)
        {
            if (track.TimedMetadataKind != TimedMetadataKind.Chapter)
            {
                continue;
            }

            foreach (IMediaCue cue in track.Cues)
            {
                if (cue is ChapterCue chapter)
                {
                    cues.Add(chapter);
                }
            }
        }

        _chapters.Clear();
        _chapters.AddRange(ChapterHelper.Sanitize(cues, player.NaturalDuration));
        player.Chapter = _chapters.FirstOrDefault();
    }
}
