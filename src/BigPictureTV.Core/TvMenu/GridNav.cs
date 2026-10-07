namespace BigPictureTV.Core.TvMenu;

/// <summary>Moving the selection around a grid of tiles filled row by row.</summary>
public static class GridNav
{
    /// <summary>The tile selected after moving; stays put at the edges.</summary>
    public static int Move(int index, int count, int columns, PadAction direction)
    {
        if (count <= 0) return 0;
        index = Math.Clamp(index, 0, count - 1);
        columns = Math.Max(1, columns);
        int column = index % columns;
        return direction switch
        {
            PadAction.Left when column > 0 => index - 1,
            PadAction.Right when column < columns - 1 && index + 1 < count => index + 1,
            PadAction.Up when index - columns >= 0 => index - columns,
            // Down from a row above a shorter last row lands on that row's last tile.
            PadAction.Down when index + columns < count => index + columns,
            PadAction.Down when index / columns < (count - 1) / columns => count - 1,
            _ => index,
        };
    }

    /// <summary>Columns for the menu: up to four tiles per row.</summary>
    public static int Columns(int count) => Math.Clamp(count, 1, 4);
}
