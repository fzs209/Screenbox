using Windows.Foundation.Collections;
using Windows.Media.Core;

namespace Screenbox.Core.Playback;

/// <summary>
/// Video track list backed by a system <see cref="MediaPlaybackItem"/>'s
/// video tracks. Selecting a track here selects it on the system item.
/// </summary>
public sealed partial class MfVideoTrackList : SingleSelectTrackList<VideoTrack>
{
    private readonly MediaPlaybackItem _item;

    public MfVideoTrackList(MediaPlaybackItem item)
    {
        _item = item;
        _item.VideoTracksChanged += OnTracksChanged;
        Refresh();
        SelectedIndexChanged += OnSelectedIndexChanged;
    }

    internal void Detach()
    {
        _item.VideoTracksChanged -= OnTracksChanged;
        SelectedIndexChanged -= OnSelectedIndexChanged;
    }

    public override void Refresh()
    {
        string? selectedId = SelectedIndex >= 0 && SelectedIndex < Count ? this[SelectedIndex].Id : null;

        TrackList.Clear();
        foreach (Windows.Media.Core.VideoTrack track in _item.VideoTracks)
        {
            TrackList.Add(new VideoTrack(track));
        }

        int systemSelected = _item.VideoTracks.SelectedIndex;
        if (systemSelected >= 0 && systemSelected < Count)
        {
            SelectedIndex = systemSelected;
        }
        else if (selectedId != null)
        {
            SelectedIndex = TrackList.FindIndex(t => t.Id == selectedId);
        }
        else
        {
            SelectedIndex = Count > 0 ? 0 : -1;
        }
    }

    private void OnTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args)
    {
        Refresh();
    }

    private void OnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        if (SelectedIndex >= 0 && _item.VideoTracks.SelectedIndex != SelectedIndex)
        {
            _item.VideoTracks.SelectedIndex = SelectedIndex;
        }
    }
}
