using System;
using System.Threading.Tasks;
using Screenbox.Core.Events;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using SystemMediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace Screenbox.Core.Playback;

/// <summary>
/// Media Foundation implementation of <see cref="IMediaPlayer"/>.
/// Wraps <see cref="SystemMediaPlayer"/> (the system playback pipeline — the
/// same pipeline Windows Media Player uses) and its playback session.
/// </summary>
public sealed class MfMediaPlayer : IMediaPlayer
{
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaEnded;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaFailed;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaOpened;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? IsMutedChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? VolumeChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<IPlaybackItem?>>? PlaybackItemChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingProgressChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingStarted;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingEnded;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? NaturalDurationChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? NaturalVideoSizeChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? CanSeekChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? PositionChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<ChapterCue?>>? ChapterChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<MediaPlaybackState>>? PlaybackStateChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? PlaybackRateChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? SubtitleDelayChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? AudioDelayChanged;

    public ChapterCue? Chapter
    {
        get => _chapter;
        set
        {
            if (value == _chapter) return;
            ChapterCue? oldValue = _chapter;
            _chapter = value;
            ChapterChanged?.Invoke(this, new ValueChangedEventArgs<ChapterCue?>(value, oldValue));
        }
    }

    public TimeSpan NaturalDuration
    {
        get => _naturalDuration;
        private set
        {
            // Length can fluctuate during playback. Check for tolerance here.
            if (Math.Abs((_naturalDuration - value).TotalMilliseconds) <= 50) return;
            TimeSpan oldValue = _naturalDuration;
            _naturalDuration = value;
            NaturalDurationChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(value, oldValue));
        }
    }

    public TimeSpan Position
    {
        get => _player.Source is null ? _position : _session.Position;
        set
        {
            if (_player.Source is null) return;
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            if (NaturalDuration > TimeSpan.Zero && value > NaturalDuration) value = NaturalDuration;

            if (_hasEnded)
            {
                if (value == NaturalDuration)
                {
                    _position = value;
                    return;
                }

                _hasEnded = false;
                TimeSpan oldValue = _position;
                _position = value;
                try
                {
                    _session.Position = value;
                }
                catch (Exception)
                {
                    // Seek on an ended session can fail; the position is already
                    // recorded and will be applied when playback restarts.
                }

                _player.Play();
                PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(value, oldValue));
                return;
            }

            TimeSpan previousValue = _position;
            _position = value;
            try
            {
                _session.Position = value;
            }
            catch (Exception)
            {
                // The session may reject a seek while the source is still
                // opening or is not seekable; keep the recorded position.
            }

            // Position changed will not fire if the player is paused.
            if (PlaybackState is MediaPlaybackState.Paused)
            {
                PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(value, previousValue));
            }
        }
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set
        {
            if (_player.IsMuted != value)
            {
                _player.IsMuted = value;
            }
        }
    }

    public double Volume
    {
        get => _player.Volume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_player.Volume - clamped) > 0.0001)
            {
                _player.Volume = clamped;
            }
        }
    }

    public double PlaybackRate
    {
        get => _player.PlaybackRate;
        set
        {
            if (Math.Abs(_player.PlaybackRate - value) > 0.0001)
            {
                double oldValue = _player.PlaybackRate;
                _playbackRate = value;
                _player.PlaybackRate = value;
                PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
            }
        }
    }

    /// <summary>
    /// Gets or sets the subtitle timing offset, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The system player has no direct subtitle-delay API, so the value is
    /// stored and reported but does not change playback. See MF-NOTES.md.
    /// </remarks>
    public double SubtitleDelay
    {
        get => _subtitleDelay;
        set
        {
            double oldValue = _subtitleDelay;
            if (Math.Abs(oldValue - value) > 0.0001)
            {
                _subtitleDelay = value;
                SubtitleDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
            }
        }
    }

    /// <summary>
    /// Gets or sets the audio timing offset, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The system player has no direct audio-delay API, so the value is
    /// stored and reported but does not change playback. See MF-NOTES.md.
    /// </remarks>
    public double AudioDelay
    {
        get => _audioDelay;
        set
        {
            double oldValue = _audioDelay;
            if (Math.Abs(oldValue - value) > 0.0001)
            {
                _audioDelay = value;
                AudioDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
            }
        }
    }

    /// <summary>
    /// Gets or sets the normalized source rectangle.
    /// </summary>
    /// <remarks>
    /// The value is stored and reported, but the system playback element has
    /// no crop-geometry equivalent, so it is not applied to the picture.
    /// See MF-NOTES.md.
    /// </remarks>
    public Rect NormalizedSourceRect
    {
        get => _normalizedSourceRect;
        set => _normalizedSourceRect = value;
    }

    public DeviceInformation? AudioDevice
    {
        get => _player.AudioDevice;
        set => _player.AudioDevice = value;
    }

    public MediaPlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            if (value == _playbackState) return;
            MediaPlaybackState oldValue = _playbackState;
            _playbackState = value;
            PlaybackStateChanged?.Invoke(this, new ValueChangedEventArgs<MediaPlaybackState>(value, oldValue));
        }
    }

    public IPlaybackItem? PlaybackItem
    {
        get => _playbackItem;
        set
        {
            if (ReferenceEquals(_playbackItem, value)) return;
            IPlaybackItem? oldValue = _playbackItem;
            if (value == null)
            {
                _player.Source = null;
                _playbackItem = null;
            }
            else
            {
                if (value is not MfPlaybackItem mfItem)
                    throw new NotSupportedException("Only MfPlaybackItem is supported by MfMediaPlayer.");

                _playbackItem = mfItem;
                _readyToPlay = true;
            }

            PlaybackItemChanged?.Invoke(this, new ValueChangedEventArgs<IPlaybackItem?>(value, oldValue));
        }
    }

    public bool CanSeek => _session.CanSeek;

    public bool IsLoopingEnabled { get; set; }

    public double BufferingProgress { get; private set; }

    public uint NaturalVideoHeight { get; private set; }

    public uint NaturalVideoWidth { get; private set; }

    public bool CanPause => _session.CanPause;

    internal SystemMediaPlayer SystemPlayer => _player;

    private readonly SystemMediaPlayer _player;
    private readonly MediaPlaybackSession _session;
    private readonly Rect _defaultSourceRect;
    private ChapterCue? _chapter;
    private Rect _normalizedSourceRect;
    private bool _readyToPlay;
    private bool _hasEnded;
    private bool _disposed;
    private bool _canSeek;
    private TimeSpan _naturalDuration;
    private TimeSpan _position;
    private MediaPlaybackState _playbackState;
    private double _audioDelay;
    private double _subtitleDelay;
    private double _playbackRate = 1.0;
    private MfPlaybackItem? _playbackItem;

    public MfMediaPlayer()
    {
        _player = new SystemMediaPlayer
        {
            AutoPlay = false
        };
        _session = _player.PlaybackSession;
        _defaultSourceRect = new Rect(0, 0, 1, 1);
        _normalizedSourceRect = _defaultSourceRect;

        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.VolumeChanged += (s, e) => VolumeChanged?.Invoke(this, EventArgs.Empty);
        _player.IsMutedChanged += (s, e) => IsMutedChanged?.Invoke(this, EventArgs.Empty);
        _player.PlaybackRateChanged += (s, e) =>
        {
            double newValue = _player.PlaybackRate;
            double oldValue = _playbackRate;
            if (Math.Abs(newValue - oldValue) <= 0.0001) return;
            _playbackRate = newValue;
            PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(newValue, oldValue));
        };

        _session.PlaybackStateChanged += (s, e) => PlaybackState = _session.PlaybackState;
        _session.PositionChanged += OnPositionChanged;
        _session.BufferingStarted += (s, e) => BufferingStarted?.Invoke(this, EventArgs.Empty);
        _session.BufferingEnded += (s, e) => BufferingEnded?.Invoke(this, EventArgs.Empty);
        _session.BufferingProgressChanged += (s, e) =>
        {
            double progress = _session.BufferingProgress;
            if (Math.Abs(BufferingProgress - progress) <= 0.0001) return;
            BufferingProgress = progress;
            BufferingProgressChanged?.Invoke(this, EventArgs.Empty);
        };
        _session.NaturalDurationChanged += (s, e) =>
        {
            NaturalDuration = _session.NaturalDuration;
            _playbackItem?.Chapters.Load(this);
            SyncSeekable();
        };
        _session.NaturalVideoSizeChanged += (s, e) =>
        {
            uint width = _session.NaturalVideoWidth;
            uint height = _session.NaturalVideoHeight;
            if (NaturalVideoWidth == width && NaturalVideoHeight == height) return;
            NaturalVideoWidth = width;
            NaturalVideoHeight = height;
            NaturalVideoSizeChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    private void OnPositionChanged(MediaPlaybackSession sender, object args)
    {
        TimeSpan newValue = _session.Position;
        if (newValue == _position) return;
        TimeSpan oldValue = _position;
        _position = newValue;
        PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(newValue, oldValue));
        UpdateChapter(newValue);
    }

    private void OnMediaOpened(SystemMediaPlayer sender, object args)
    {
        _hasEnded = false;
        MediaOpened?.Invoke(this, EventArgs.Empty);
        NaturalDuration = _session.NaturalDuration;
        _playbackItem?.RefreshTracks();
        _playbackItem?.Chapters.Load(this);
        SyncSeekable();
    }

    private void OnMediaEnded(SystemMediaPlayer sender, object args)
    {
        _hasEnded = true;
        MediaEnded?.Invoke(this, EventArgs.Empty);
        PlaybackState = MediaPlaybackState.None;
        if (IsLoopingEnabled)
        {
            // Escape the MediaEnded callback thread before restarting.
            Task.Run(Replay);
        }
    }

    private void OnMediaFailed(SystemMediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        MediaFailed?.Invoke(this, EventArgs.Empty);
        PlaybackState = MediaPlaybackState.None;
    }

    private void SyncSeekable()
    {
        bool seekable = _session.CanSeek;
        if (_canSeek == seekable) return;
        _canSeek = seekable;
        CanSeekChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChapter(TimeSpan position)
    {
        if (_playbackItem?.Chapters is not { Count: > 0 } chapters) return;
        ChapterCue? current = null;
        foreach (ChapterCue chapter in chapters)
        {
            if (chapter.StartTime <= position)
            {
                current = chapter;
            }
            else
            {
                break;
            }
        }

        Chapter = current;
    }

    public void AddSubtitle(IStorageFile file, bool select = true)
    {
        _playbackItem?.SubtitleTracks.AddExternalSubtitle(file, select);
    }

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;

        _player.MediaOpened -= OnMediaOpened;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Source = null;
        _player.Dispose();
    }

    public void Pause()
    {
        if (PlaybackState != MediaPlaybackState.Playing) return;
        _player.Pause();
    }

    public void Play()
    {
        if (_playbackItem == null) return;
        if (_readyToPlay)
        {
            _readyToPlay = false;
            _hasEnded = false;
            _player.Source = _playbackItem.SystemPlaybackItem;
        }

        _player.Play();
    }

    public void StepBackwardOneFrame()
    {
        try
        {
            _player.StepBackwardOneFrame();
        }
        catch (Exception)
        {
            Position -= TimeSpan.FromSeconds(.042);
        }
    }

    public void StepForwardOneFrame()
    {
        try
        {
            _player.StepForwardOneFrame();
            TimeSpan newValue = _session.Position;
            if (newValue != _position)
            {
                TimeSpan oldValue = _position;
                _position = newValue;
                PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(newValue, oldValue));
            }
        }
        catch (Exception)
        {
            Position += TimeSpan.FromSeconds(.042);
        }
    }

    public bool NextChapter()
    {
        if (_playbackItem?.Chapters is not { Count: > 0 } chapters)
            return false;

        foreach (ChapterCue chapter in chapters)
        {
            if (chapter.StartTime > Position)
            {
                Position = chapter.StartTime;
                Chapter = chapter;
                return true;
            }
        }

        return false;
    }

    public bool PreviousChapter()
    {
        if (_playbackItem?.Chapters is not { Count: > 0 } chapters)
            return false;

        ChapterCue? previous = null;
        foreach (ChapterCue chapter in chapters)
        {
            if (chapter.StartTime < Position - TimeSpan.FromSeconds(1))
            {
                previous = chapter;
            }
            else
            {
                break;
            }
        }

        if (previous is null)
            return false;

        Position = previous.StartTime;
        Chapter = previous;
        return true;
    }

    public bool TryTakeSnapshot(string destinationFolderPath, uint width = 0, uint height = 0)
    {
        // The system player has no snapshot API. See MF-NOTES.md.
        return false;
    }

    private void Replay()
    {
        _hasEnded = false;
        try
        {
            _session.Position = TimeSpan.Zero;
        }
        catch (Exception)
        {
            // Position reset can fail on an ended session; Play() below
            // restarts from the recorded position instead.
        }

        _player.Play();
    }
}
