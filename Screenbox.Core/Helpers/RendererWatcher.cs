using System;
using System.Collections.Generic;
using Screenbox.Core.Events;
using Screenbox.Core.Models;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Renderer discovery for the Media Foundation backend. The system player
/// exposes no Chromecast renderer discovery, so this watcher never finds
/// renderers and <see cref="Start"/> always reports failure. It keeps the
/// same surface the Cast UI already uses. See MF-NOTES.md.
/// </summary>
public sealed partial class RendererWatcher : IDisposable
{
#pragma warning disable CS0067 // Events are kept for the Cast UI; discovery never finds renderers.
    public event EventHandler<RendererFoundEventArgs>? RendererFound;
    public event EventHandler<RendererLostEventArgs>? RendererLost;
#pragma warning restore CS0067

    public bool IsStarted => false;

    internal RendererWatcher()
    {
    }

    public IReadOnlyList<Renderer> GetRenderers()
    {
        return Array.Empty<Renderer>();
    }

    public bool Start()
    {
        return false;
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
