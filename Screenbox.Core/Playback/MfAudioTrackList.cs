using Windows.Foundation.Collections;
using Windows.Media.Core;

namespace Screenbox.Core.Playback;

/// <summary>
/// Audio track list backed by a system <see cref="MediaPlaybackItem"/>'s
/// audio tracks. Selecting a track here selects it on the system item.
/// </summary>
public sealed partial class MfAudioTrackList : SingleSelectTrackList<AudioTrack>
{
    private readonly MediaPlaybackItem _item;

    public MfAudioTrackList(MediaPlaybackItem item)
    {
        _item = item;
        _item.AudioTracksChanged += OnTracksChanged;
        Refresh();
        SelectedIndexChanged += OnSelectedIndexChanged;
    }

    internal void Detach()
    {
        _item.AudioTracksChanged -= OnTracksChanged;
        SelectedIndexChanged -= OnSelectedIndexChanged;
    }

    public override void Refresh()
    {
        string? selectedId = SelectedIndex >= 0 && SelectedIndex < Count ? this[SelectedIndex].Id : null;

        TrackList.Clear();
        foreach (Windows.Media.Core.AudioTrack track in _item.AudioTracks)
        {
            TrackList.Add(new AudioTrack(track));
        }

        int systemSelected = _item.AudioTracks.SelectedIndex;
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
        if (SelectedIndex >= 0 && _item.AudioTracks.SelectedIndex != SelectedIndex)
        {
            _item.AudioTracks.SelectedIndex = SelectedIndex;
        }
    }
}
