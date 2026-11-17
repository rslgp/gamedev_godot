using System;
using Godot;

namespace ReusableComponents.MultiMeshBatching;

/// <summary>Draws up to <see cref="Capacity"/> textured quads in ONE draw call: fill instances each frame with
/// <see cref="Begin"/> / <see cref="Add"/> / <see cref="Commit"/> and the whole buffer is uploaded with a single
/// <c>RenderingServer.MultimeshSetBuffer</c>. Writing the buffer directly leaves the MultiMesh culling AABB stale
/// (it stays at the first instance, so everything disappears once that rect leaves the screen); this node
/// always sets <c>CustomAabb</c> from <see cref="CullingBounds"/> to avoid it. Zero allocation per frame.
/// Intended as a read-only VIEW of simulation state: fill it from the sim each frame, never store gameplay in it.</summary>
[GlobalClass]
public partial class MultiMeshBatch2D : MultiMeshInstance2D
{
    /// <summary>Floats per instance in the buffer: 2D transform (8) + color (4).</summary>
    public const int FloatsPerInstance = 12;

    float[] _buf = [];
    int _count;
    bool _ready;

    /// <summary>Maximum instances (set before the node enters the tree; changing it later rebuilds the buffer).</summary>
    [Export] public int Capacity { get; set; } = 256;

    /// <summary>Quad size in pixels.</summary>
    [Export] public Vector2 QuadSize { get; set; } = new(4, 4);

    /// <summary>World-space area instances may occupy (culling bounds). Instances outside it may be culled.</summary>
    [Export] public Rect2 CullingBounds { get; set; } = new(-4096, -4096, 8192, 8192);

    /// <summary>Instances in the last committed frame.</summary>
    public int VisibleCount { get; private set; }

    /// <summary>Instances added since <see cref="Begin"/>.</summary>
    public int PendingCount => _count;

    /// <summary>Adds beyond <see cref="Capacity"/> since construction (dropped, never thrown).</summary>
    public int Overflow { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        if (Capacity < 0) { GD.PushError("MultiMeshBatch2D: Capacity must be >= 0"); Capacity = 0; }
        Rebuild();
    }

    /// <summary>(Re)creates the MultiMesh and buffer from the exports. Called by <c>_Ready</c>; call again after
    /// changing <see cref="Capacity"/>, <see cref="QuadSize"/> or <see cref="CullingBounds"/> at runtime.</summary>
    public void Rebuild()
    {
        var b = CullingBounds;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseColors = true,
            Mesh = new QuadMesh { Size = QuadSize }, InstanceCount = Math.Max(0, Capacity), VisibleInstanceCount = 0,
            CustomAabb = new Aabb(new Vector3(b.Position.X, b.Position.Y, -1), new Vector3(b.Size.X, b.Size.Y, 2)),
        };
        _buf = new float[Math.Max(0, Capacity) * FloatsPerInstance];
        _count = 0; VisibleCount = 0;
        _ready = true;
    }

    /// <summary>Starts a frame (forgets the previous frame's instances; the buffer is reused).</summary>
    public void Begin() => _count = 0;

    /// <summary>Adds one quad centered at <paramref name="position"/> (local pixels).</summary>
    /// <param name="position">Center.</param>
    /// <param name="color">Modulate color (multiplied with <c>Texture</c>).</param>
    /// <returns>False when full (counted in <see cref="Overflow"/>).</returns>
    public bool Add(Vector2 position, Color color)
    {
        if (_count * FloatsPerInstance >= _buf.Length) { Overflow++; return false; }
        int o = _count++ * FloatsPerInstance;
        var buf = _buf;
        buf[o] = 1; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = position.X;
        buf[o + 4] = 0; buf[o + 5] = 1; buf[o + 6] = 0; buf[o + 7] = position.Y;
        buf[o + 8] = color.R; buf[o + 9] = color.G; buf[o + 10] = color.B; buf[o + 11] = color.A;
        return true;
    }

    /// <summary>Uploads the frame in one call and shows exactly <see cref="PendingCount"/> instances.</summary>
    public void Commit()
    {
        if (!_ready) { GD.PushError("MultiMeshBatch2D: Commit before _Ready/Rebuild"); return; }
        if (_buf.Length > 0) RenderingServer.MultimeshSetBuffer(Multimesh.GetRid(), _buf);
        Multimesh.VisibleInstanceCount = _count;
        VisibleCount = _count;
    }
}
