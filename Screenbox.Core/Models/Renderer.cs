namespace Screenbox.Core.Models;

/// <summary>
/// A network renderer that can receive cast playback. The Media Foundation
/// backend discovers no renderers, so instances are never produced at
/// runtime; the model is kept for the Cast UI. See MF-NOTES.md.
/// </summary>
public sealed partial class Renderer
{
    public bool IsAvailable { get; }

    public string Name { get; }

    public string Type { get; }

    public string? IconUri { get; }

    public bool CanRenderVideo { get; }

    public bool CanRenderAudio { get; }

    public Renderer(
        string name,
        string type,
        string? iconUri = null,
        bool canRenderVideo = false,
        bool canRenderAudio = false,
        bool isAvailable = false)
    {
        Name = name;
        Type = type;
        IconUri = iconUri;
        CanRenderVideo = canRenderVideo;
        CanRenderAudio = canRenderAudio;
        IsAvailable = isAvailable;
    }

    public override string ToString()
    {
        return $"{Name}, {Type}";
    }
}
