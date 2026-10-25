namespace Baudacious.Render;

/// <summary>
/// Tracks how far behind the screen the displayed image is while a scroll animates.
/// </summary>
/// <remarks>
/// DECSCLM on a VT100 advanced the raster a couple of scan lines per refresh instead of a
/// whole character cell at once. This is the same idea kept as a single number: a debt, in
/// source pixels, that a scroll adds to and the clock drains. The renderer draws the grid
/// shifted up by whatever is left.
/// <para>
/// The debt never exceeds one cell. That is partly honesty - the governor owns the rate at
/// which bytes arrive, and a second thing throttling the same stream would make the line
/// rate a lie, so output that outruns the animation makes the scroll snap rather than the
/// output wait. It is also a hard constraint: the renderer draws exactly one row above the
/// viewport, so a debt of two lines would have nothing to put in the gap.
/// </para>
/// <para>
/// No GL here on purpose. The arithmetic is the part worth testing.
/// </para>
/// </remarks>
public sealed class SmoothScroll
{
    private double _debt;

    /// <summary>Source pixels the grid should be drawn above where it belongs.</summary>
    public double Offset => _debt;

    /// <summary>Whether a scroll is part-way through, and the frame therefore not final.</summary>
    public bool IsAnimating => _debt > 0;

    /// <summary>
    /// Records that the screen scrolled, putting the display that many lines behind.
    /// </summary>
    public void Scrolled(long lines, int cellHeight)
    {
        if (lines <= 0 || cellHeight <= 0)
        {
            return;
        }

        _debt = Math.Min(cellHeight, _debt + ((double)lines * cellHeight));
    }

    /// <summary>
    /// Lets the clock catch the display up, at <paramref name="linesPerSecond"/>.
    /// A rate of zero means the jump scroll, and leaves no offset to draw.
    /// </summary>
    public void Advance(TimeSpan delta, double linesPerSecond, int cellHeight)
    {
        if (_debt <= 0)
        {
            return;
        }

        if (linesPerSecond <= 0 || cellHeight <= 0 || delta <= TimeSpan.Zero)
        {
            // Not an early return: a rate of zero has to land the scroll, not hold it.
            _debt = linesPerSecond <= 0 || cellHeight <= 0 ? 0 : _debt;
            return;
        }

        _debt -= linesPerSecond * cellHeight * delta.TotalSeconds;

        // Exactly zero rather than a residue, so a settled screen renders identically to
        // one drawn with the feature switched off.
        if (_debt < 0.01)
        {
            _debt = 0;
        }
    }

    /// <summary>
    /// Pixels to raise the grid by when it has been emitted one row lower than it belongs.
    /// </summary>
    /// <remarks>
    /// The complement of the debt, and the one piece of arithmetic here that is easy to
    /// get backwards. The renderer draws the grid a whole row down so the line that left
    /// the top has somewhere to be; this is what cancels that row again. At a full cell
    /// behind nothing is cancelled, and the screen looks exactly as it did before the line
    /// arrived. At zero the whole row is cancelled and the grid is where it always was.
    /// <para>
    /// Getting the sign wrong runs the scroll downwards and ends it with a jump, and a
    /// still frame taken halfway through looks identical either way, so it is worth a test
    /// rather than an eye.
    /// </para>
    /// </remarks>
    public static double LiftFor(double behind, int cellHeight) => cellHeight - behind;

    /// <summary>
    /// Abandons any animation in progress.
    /// </summary>
    /// <remarks>
    /// For resizes, font scale changes and session switches. An offset measured in the
    /// pixels of one grid means nothing in another, and a row that was above the viewport
    /// before a reflow may not be the row above it after.
    /// </remarks>
    public void Reset() => _debt = 0;
}
