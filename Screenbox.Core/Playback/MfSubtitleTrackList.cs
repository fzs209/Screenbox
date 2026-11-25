using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Playback;

/// <summary>
/// Subtitle track list backed by a system <see cref="MediaPlaybackItem"/>'s
/// timed metadata tracks (subtitle and caption kinds only). Selecting a
/// track presents it and disables the others. External subtitle files are
/// loaded lazily through <see cref="TimedTextSource"/>.
/// </summary>
public sealed partial class MfSubtitleTrackList : SingleSelectTrackList<SubtitleTrack>, ISubtitleTrackList
{
    private sealed class LazySubtitleTrack
    {
        public SubtitleTrack Track { get; }

        public StorageFile File { get; }

        public bool Loading { get; set; }

        public LazySubtitleTrack(StorageFile file)
        {
            File = file;
            Track = new SubtitleTrack("-1", file.Name);
        }
    }

    private readonly MediaPlaybackItem _item;
    private readonly MfPlaybackItem _owner;
    private readonly List<LazySubtitleTrack> _pendingTracks = new();
    private readonly List<uint> _winrtIndices = new();
    private readonly HashSet<string> _knownTrackIds = new();
    private bool _selectPendingExternal;

    public MfSubtitleTrackList(MediaPlaybackItem item, MfPlaybackItem owner)
    {
        _item = item;
        _owner = owner;
        _item.TimedMetadataTracksChanged += OnTracksChanged;
        Refresh();
        SelectedIndexChanged += OnSelectedIndexChanged;
    }

    internal void Detach()
    {
        _item.TimedMetadataTracksChanged -= OnTracksChanged;
        SelectedIndexChanged -= OnSelectedIndexChanged;
    }

    public override void Refresh()
    {
        SubtitleTrack? selectedPlaceholder = null;
        string? selectedId = null;
        if (SelectedIndex >= 0 && SelectedIndex < Count && SelectedIndex < _winrtIndices.Count)
        {
            if (_winrtIndices[SelectedIndex] == uint.MaxValue)
            {
                selectedPlaceholder = TrackList[SelectedIndex];
            }
            else
            {
                selectedId = TrackList[SelectedIndex].Id;
            }
        }

        List<(SubtitleTrack Track, uint WinrtIndex, bool Presented)> realTracks = new();
        for (int i = 0; i < _item.TimedMetadataTracks.Count; i++)
        {
            TimedMetadataTrack track = _item.TimedMetadataTracks[i];
            if (track.TimedMetadataKind is TimedMetadataKind.Subtitle or TimedMetadataKind.Caption)
            {
                bool presented = _item.TimedMetadataTracks.GetPresentationMode((uint)i)
                    == TimedMetadataTrackPresentationMode.PlatformPresented;
                realTracks.Add((new SubtitleTrack(track), (uint)i, presented));
            }
        }

        List<string> newTrackIds = realTracks
            .Select(t => t.Track.Id)
            .Where(id => !_knownTrackIds.Contains(id))
            .ToList();

        TrackList.Clear();
        _winrtIndices.Clear();
        foreach ((SubtitleTrack track, uint winrtIndex, _) in realTracks)
        {
            TrackList.Add(track);
            _winrtIndices.Add(winrtIndex);
        }

        int newSelectedIndex = -1;
        bool selectNewExternal = _selectPendingExternal && newTrackIds.Count > 0;
        int presentedIndex = realTracks.FindIndex(t => t.Presented);
        if (selectNewExternal)
        {
            newSelectedIndex = TrackList.FindIndex(t => t.Id == newTrackIds[0]);
            _selectPendingExternal = false;
            if (_pendingTracks.Count > 0)
            {
                _pendingTracks.RemoveAt(0);
            }
        }
        else if (presentedIndex >= 0)
        {
            newSelectedIndex = presentedIndex;
        }
        else if (selectedId != null)
        {
            newSelectedIndex = TrackList.FindIndex(t => t.Id == selectedId);
        }

        foreach (LazySubtitleTrack lazy in _pendingTracks)
        {
            TrackList.Add(lazy.Track);
            _winrtIndices.Add(uint.MaxValue);
        }

        if (!selectNewExternal && selectedPlaceholder != null)
        {
            int placeholderIndex = TrackList.FindIndex(t => ReferenceEquals(t, selectedPlaceholder));
            if (placeholderIndex >= 0)
            {
                newSelectedIndex = placeholderIndex;
            }
        }

        _knownTrackIds.Clear();
        foreach ((SubtitleTrack track, _, _) in realTracks)
        {
            _knownTrackIds.Add(track.Id);
        }

        SelectedIndex = newSelectedIndex;
        ApplySelection();
    }

    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    public void AddExternalSubtitle(IStorageFile file, bool select = true)
    {
        if (file is StorageFile storageFile)
        {
            AddExternalSubtitleInternal(storageFile, select);
        }
    }

    private void AddExternalSubtitleInternal(StorageFile file, bool select)
    {
        LazySubtitleTrack? existing = _pendingTracks.FirstOrDefault(x =>
            string.Equals(x.File.Path, file.Path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (select)
            {
                int existingIndex = TrackList.FindIndex(t => ReferenceEquals(t, existing.Track));
                if (existingIndex >= 0)
                {
                    SelectedIndex = existingIndex;
                }
            }

            return;
        }

        LazySubtitleTrack lazy = new(file);
        _pendingTracks.Add(lazy);
        TrackList.Add(lazy.Track);
        _winrtIndices.Add(uint.MaxValue);

        if (select)
        {
            SelectedIndex = TrackList.Count - 1;
        }
    }

    private void OnTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args)
    {
        Refresh();
    }

    private void OnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        if (SelectedIndex >= 0 && SelectedIndex < _winrtIndices.Count
            && _winrtIndices[SelectedIndex] == uint.MaxValue)
        {
            LazySubtitleTrack? lazy = _pendingTracks.FirstOrDefault(
                x => ReferenceEquals(x.Track, TrackList[SelectedIndex]));
            if (lazy is { Loading: false })
            {
                lazy.Loading = true;
                _ = LoadExternalSubtitleAsync(lazy);
            }

            return;
        }

        ApplySelection();
    }

    private async Task LoadExternalSubtitleAsync(LazySubtitleTrack lazy)
    {
        try
        {
            IRandomAccessStream stream = await lazy.File.OpenReadAsync();
            _owner.KeepExternalSubtitleStream(stream);
            TimedTextSource timedText = TimedTextSource.CreateFromStream(stream);
            _owner.SystemMediaSource.ExternalTimedTextSources.Add(timedText);
            _selectPendingExternal = true;
            // The new track normally arrives through TimedMetadataTracksChanged;
            // refresh now as well in case it is already present.
            Refresh();
        }
        catch (Exception)
        {
            lazy.Loading = false;
            _selectPendingExternal = false;
        }
    }

    private void ApplySelection()
    {
        for (int i = 0; i < _winrtIndices.Count; i++)
        {
            uint winrtIndex = _winrtIndices[i];
            if (winrtIndex == uint.MaxValue) continue;
            _item.TimedMetadataTracks.SetPresentationMode(
                winrtIndex,
                i == SelectedIndex
                    ? TimedMetadataTrackPresentationMode.PlatformPresented
                    : TimedMetadataTrackPresentationMode.Disabled);
        }
    }
}
