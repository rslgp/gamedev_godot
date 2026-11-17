using Godot;
using FileAccess = Godot.FileAccess;

namespace ReusableComponents.SaveStore;

/// <summary>Text save files through Godot's <see cref="FileAccess"/> (works with <c>user://</c> on every
/// platform, including web/mobile sandboxes). Writes are atomic: content goes to <c>&lt;path&gt;.tmp</c> and
/// is renamed over the target, so a crash mid-write never leaves a truncated save. Never throws; errors
/// come back as text. Engine thread only.</summary>
public static class UserTextStore
{
    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> atomically (creates the folder).</summary>
    /// <param name="path"><c>user://</c> (recommended) or absolute path.</param>
    /// <param name="text">Content (UTF-8).</param>
    /// <returns>Null on success, else an error description.</returns>
    public static string? Write(string path, string text)
    {
        if (string.IsNullOrEmpty(path)) return "empty path";
        var dir = path.GetBaseDir();
        if (dir.Length > 0 && !DirAccess.DirExistsAbsolute(dir))
        {
            var mk = DirAccess.MakeDirRecursiveAbsolute(dir);
            if (mk != Error.Ok) return $"cannot create {dir}: {mk}";
        }
        var tmp = path + ".tmp";
        using (var f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (f == null) return $"cannot open {tmp}: {FileAccess.GetOpenError()}";
            f.StoreString(text ?? "");
            f.Flush();
            if (f.GetError() != Error.Ok) return $"write failed: {f.GetError()}";
        }
        var rn = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(tmp), ProjectSettings.GlobalizePath(path));
        if (rn != Error.Ok)
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(tmp));
            return $"rename failed: {rn}";
        }
        return null;
    }

    /// <summary>Reads a whole text file.</summary>
    /// <param name="path"><c>user://</c>, <c>res://</c> or absolute path.</param>
    /// <param name="text">Content, or "" on failure.</param>
    /// <param name="error">Null on success, "missing" when the file does not exist, else a description.</param>
    /// <returns>True on success.</returns>
    public static bool TryRead(string path, out string text, out string? error)
    {
        text = "";
        if (string.IsNullOrEmpty(path) || !FileAccess.FileExists(path)) { error = "missing"; return false; }
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) { error = $"cannot open: {FileAccess.GetOpenError()}"; return false; }
        text = f.GetAsText();
        error = null;
        return true;
    }

    /// <summary>Deletes a file if it exists.</summary>
    /// <param name="path"><c>user://</c> or absolute path.</param>
    /// <returns>True when the file is gone afterwards.</returns>
    public static bool Delete(string path)
    {
        if (!FileAccess.FileExists(path)) return true;
        return DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)) == Error.Ok;
    }
}
