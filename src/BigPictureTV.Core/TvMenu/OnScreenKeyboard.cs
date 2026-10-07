namespace BigPictureTV.Core.TvMenu;

public enum KeyKind
{
    Text,
    Space,
    Backspace,
    Enter,     // "Done": types Enter and closes the keyboard
    Shift,     // capitals on or off
    Symbols,   // letters or symbols
    Close,
}

/// <summary>One key: what it does, what it shows, and how many columns wide it is.</summary>
public sealed record KeyCap(KeyKind Kind, string Label, int Width = 1)
{
    public static KeyCap Char(string text) => new(KeyKind.Text, text);
}

/// <summary>What pressing a key asks the app to type, or Close to hide the keyboard.</summary>
public readonly record struct KeyOutput(KeyKind Kind, string Text = "");

/// <summary>
/// An on-screen keyboard driven by the controller, like a console's: a grid
/// of keys 10 columns wide, letters or symbols, with capitals. Holds only the
/// state (which key is chosen, capitals, symbols); the app draws it and types
/// what <see cref="Press"/> returns into the window in front. Not thread-safe.
/// </summary>
public sealed class OnScreenKeyboard
{
    public const int Columns = 10;

    static KeyCap[] BottomRow(string symbolsLabel) => new[]
    {
        new KeyCap(KeyKind.Shift, "⇧", 2),
        new KeyCap(KeyKind.Symbols, symbolsLabel, 2),
        new KeyCap(KeyKind.Space, "␣", 3),
        new KeyCap(KeyKind.Backspace, "⌫", 1),
        new KeyCap(KeyKind.Enter, "OK", 2),
    };

    static KeyCap[] Chars(string keys) => keys.Split(' ').Select(KeyCap.Char).ToArray();

    static readonly KeyCap[][] LetterRows =
    {
        Chars("1 2 3 4 5 6 7 8 9 0"),
        Chars("q w e r t y u i o p"),
        Chars("a s d f g h j k l ñ"),
        Chars("z x c v b n m , . -"),
        BottomRow("&123"),
    };

    static readonly KeyCap[][] SymbolRows =
    {
        Chars("1 2 3 4 5 6 7 8 9 0"),
        Chars("@ # $ % & * ( ) ' \""),
        Chars("! ? ¿ ¡ : ; / _ + ="),
        Chars("á é í ó ú ü < > [ ]"),
        BottomRow("abc"),
    };

    public bool Capitals { get; private set; }
    public bool ShowingSymbols { get; private set; }
    public int Row { get; private set; } = 1;
    public int Index { get; private set; }

    public IReadOnlyList<IReadOnlyList<KeyCap>> Rows => ShowingSymbols ? SymbolRows : LetterRows;

    public KeyCap Selected => Rows[Row][Index];

    /// <summary>What a key shows now (capitals applied to letters).</summary>
    public string LabelOf(KeyCap key) => key.Kind == KeyKind.Text && Capitals ? key.Label.ToUpperInvariant() : key.Label;

    /// <summary>Back to the first letter, in lower case, as when opened.</summary>
    public void Reset()
    {
        Capitals = false;
        ShowingSymbols = false;
        Row = 1;
        Index = 0;
    }

    /// <summary>Moves the choice; left and right wrap around the row, up and down keep the column.</summary>
    public void Move(PadAction direction)
    {
        var row = Rows[Row];
        switch (direction)
        {
            case PadAction.Left:
                Index = (Index + row.Count - 1) % row.Count;
                break;
            case PadAction.Right:
                Index = (Index + 1) % row.Count;
                break;
            case PadAction.Up or PadAction.Down:
                int target = direction == PadAction.Up ? Row - 1 : Row + 1;
                if (target < 0 || target >= Rows.Count) return;
                double centre = Start(row, Index) + row[Index].Width / 2.0;
                Row = target;
                Index = KeyAt(Rows[target], centre);
                break;
        }
    }

    /// <summary>Presses the chosen key. Null when it only changes the keyboard (capitals, symbols).</summary>
    public KeyOutput? Press() => Press(Selected);

    public KeyOutput? Press(KeyCap key)
    {
        switch (key.Kind)
        {
            case KeyKind.Shift:
                ToggleCapitals();
                return null;
            case KeyKind.Symbols:
                ToggleSymbols();
                return null;
            case KeyKind.Text:
                // Capitals last one letter, as on a phone or a console.
                string text = LabelOf(key);
                if (Capitals && char.IsLetter(text, 0)) Capitals = false;
                return new KeyOutput(KeyKind.Text, text);
            case KeyKind.Space:
                return new KeyOutput(KeyKind.Text, " ");
            default:
                return new KeyOutput(key.Kind);
        }
    }

    public void ToggleCapitals() => Capitals = !Capitals;

    public void ToggleSymbols()
    {
        ShowingSymbols = !ShowingSymbols;
        Index = Math.Min(Index, Rows[Row].Count - 1);
    }

    static int Start(IReadOnlyList<KeyCap> row, int index)
    {
        int start = 0;
        for (int i = 0; i < index; i++) start += row[i].Width;
        return start;
    }

    static int KeyAt(IReadOnlyList<KeyCap> row, double column)
    {
        int start = 0;
        for (int i = 0; i < row.Count; i++)
        {
            if (column < start + row[i].Width) return i;
            start += row[i].Width;
        }
        return row.Count - 1;
    }
}
