namespace Baudacious.App;

/// <summary>Fitting a path into a fixed number of cells.</summary>
public static class Paths
{
    /// <summary>
    /// Shortens a path to at most <paramref name="width"/> characters, keeping the end.
    /// </summary>
    /// <remarks>
    /// The end, because that is the half that identifies it: everyone's path starts
    /// C:\Users\somebody\AppData and only the tail says which folder this is. Three dots
    /// rather than an ellipsis, since CP437 has no ellipsis and the atlas would draw a
    /// solid square in its place.
    /// </remarks>
    public static string Shorten(string? path, int width)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= width)
        {
            return path ?? string.Empty;
        }

        return width <= 3 ? path[^width..] : "..." + path[^(width - 3)..];
    }
}
