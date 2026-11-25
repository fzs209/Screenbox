using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Services;

/// <summary>
/// Cast service for the Media Foundation backend. The system player has no
/// Chromecast renderer discovery, so discovery finds nothing and activating
/// a renderer always fails. The Cast UI stays wired but inert.
/// See MF-NOTES.md.
/// </summary>
public sealed class CastService : ICastService
{
    public RendererWatcher CreateRendererWatcher(IMediaPlayer player)
    {
        _ = player;
        return new RendererWatcher();
    }

    public bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer)
    {
        _ = player;
        _ = renderer;
        return false;
    }
}
