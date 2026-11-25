# Media Foundation backend notes

This branch (`mf-backend`) replaces the LibVLC playback backend with the
Windows system media pipeline: `Windows.Media.Playback.MediaPlayer` for
playback and `Windows.Media.Core.MediaSource` / `MediaPlaybackItem` for
media items and tracks. The rest of the app (UI, library, queue, settings
storage) is unchanged; the new backend implements the same
`IMediaPlayer` / `IPlaybackItem` contracts the previous backend did.

The video surface is now the system `MediaPlayerElement` control. The
previous custom Direct3D swap-chain view (`Screenbox.VideoView`) is gone,
along with the LibVLC packages.

## Gains

- Playback goes through the system pipeline, the same one Windows Media
  Player uses. MP4 Dolby Vision Profile 5/8 content should put a
  Dolby Vision display into Dolby Vision mode, as it does in Windows
  Media Player.
- Decoding uses the system hardware decoders and system codec extensions.
- No bundled libvlc native libraries; the package is smaller and there is
  no separate codec stack to keep in sync.

## Cuts and behavior changes

- **Format breadth.** Only formats the installed system codecs can play
  will work. Containers and streams the previous backend handled on its
  own (for example some MKV, FLAC, and network-stream variants) may fail
  or need a codec extension from the Microsoft Store.
- **Chromecast.** The system player exposes no Chromecast renderer
  discovery. Cast discovery finds nothing and activating a renderer
  always fails; the Cast UI is still present but inert.
- **Audio and subtitle delay.** The system player has no direct
  audio-delay or subtitle-delay API. The `IMediaPlayer` values are stored
  and their change events still fire, but they do not change playback.
- **Snapshot.** `TryTakeSnapshot` returns `false`; the system player has
  no snapshot API.
- **Crop / aspect ratio.** `NormalizedSourceRect` is stored but not
  applied; the system playback element has no crop-geometry equivalent,
  so aspect-ratio changes letterbox instead of cropping.
- **Chapters.** The system player has no chapter-description API.
  Chapters are shown only when the media source exposes chapter cues
  through a chapter timed-metadata track; otherwise the chapter list is
  empty.
- **Playlist sub-item expansion.** `ExpandSubItemsAsync` returns an empty
  list. The system media source does not expand a container into
  sub-items; expandable containers are still handled by their own source
  implementations (for example M3U playlists).
- **Playback options.** Per-file and global command-line options belonged
  to the previous backend. The stored settings values are kept, and the
  options dialogs still edit them, but the Media Foundation backend
  ignores them. The video-upscale setting is likewise stored and unused.
- **External subtitles** are loaded through `TimedTextSource` and added
  to the media source's external timed-text sources. Formats the system
  timed-text stack cannot parse will not appear as tracks.
- **File access.** Playback items are created synchronously. When a file
  source has no resolved `StorageFile`, the backend resolves it from its
  access token or path on a background thread (5 second budget) and falls
  back to opening the file path as a URI if that does not finish.

## Device check

Dolby Vision output can only be judged by the display itself (its
Dolby Vision badge / mode), not by any in-app diagnostic. Compare the
same MP4 file in this build and in Windows Media Player on a
Dolby Vision display.
