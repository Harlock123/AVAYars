namespace AVAYars.Engine;

/// <summary>
/// The screen: the score along the top, and below it the playfield, which wraps from top to bottom. The Neutral
/// Zone is a band down the middle, the Qotile and its shield are on the right.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 0, Right = 1024, Top = 64, Bottom = 744;
    public const double PlayHeight = Bottom - Top;
    /// <summary>The Neutral Zone, from side to side.</summary>
    public const double ZoneLeft = 392, ZoneRight = 488;
    /// <summary>One chunky pixel of a sprite.</summary>
    public const double Pixel = 4;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: a way to fly (each of Dx and Dy -1, 0 or 1; Dy +1 is down the screen), and the fire button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The two shields, a level each in turn.</summary>
public enum Shape
{
    /// <summary>A thick arch of cells round the Qotile, still on it.</summary>
    Arch,
    /// <summary>A band of cells that runs round and round like a conveyor.</summary>
    Band,
}

/// <summary>The Qotile at home on its pad; turned into the Swirl, spinning, about to go; the Swirl in flight.</summary>
public enum SwirlState { Home, Pad, Flying }

/// <summary>No Zorlon Cannon; the cannon at the left edge, following the Yar up and down; its shot crossing the screen.</summary>
public enum CannonState { None, Ready, Fired }

/// <summary>
/// The shield's shapes, as a grid of cells. It stands in front of the Qotile and goes up and down with it. The arch
/// stays as it is; the band's cells run along it, top right round to bottom right and back again.
/// </summary>
public static class Shield
{
    public const int Rows = 16, Cols = 8;
    public const double CellW = 16, CellH = 20, X = 872;
    public const double Height = Rows * CellH;
    /// <summary>The Qotile's place in the shield: its middle, from the shield's top left.</summary>
    public const double QotileDx = 6 * CellW, QotileDy = 8 * CellH;
    public const double QotileHalfW = 16, QotileHalfH = 20;

    public static readonly string[] Arch =
    [
        "....####",
        "..######",
        ".####...",
        ".###....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        ".###....",
        ".####...",
        "..######",
        "....####",
    ];

    public static readonly string[] Band =
    [
        "########",
        "########",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "##......",
        "########",
        "########",
    ];

    public static string[] Mask(Shape s) => s == Shape.Arch ? Arch : Band;

    /// <summary>The band's two lanes, each in the order its cells run: in along the top, down the front, out along the bottom.</summary>
    public static readonly (int R, int C)[][] Lanes = [Lane(0), Lane(1)];

    private static (int R, int C)[] Lane(int k)
    {
        var lane = new List<(int, int)>();
        for (int c = Cols - 1; c >= k; c--) lane.Add((k, c));
        for (int r = k + 1; r < Rows - 1 - k; r++) lane.Add((r, k));
        for (int c = k; c < Cols; c++) lane.Add((Rows - 1 - k, c));
        return lane.ToArray();
    }

    public static int Count(Shape s) => Mask(s).Sum(row => row.Count(ch => ch == '#'));
}

/// <summary>
/// Everything that moves or changes, kept together so it can be copied whole: the shield and the Qotile, the Yar, its
/// shot, the Zorlon Cannon, the Destroyer Missile, the Swirl and the score.
/// </summary>
public sealed class World
{
    public int Level = 1;
    public Shape Shape;
    /// <summary>Which cells of the shield are still there, by row and column.</summary>
    public bool[,] Cells = new bool[Shield.Rows, Shield.Cols];
    public double ShieldY = 248, ShieldVy = 50;
    /// <summary>Time toward the band's next step round.</summary>
    public double Turn;

    /// <summary>The Yar: where (its middle), and the way it faces.</summary>
    public double X, Y;
    public int Fx = 1, Fy;
    public bool Moving;
    public bool Dead;
    public string Cause = "";

    /// <summary>The Yar's energy missile.</summary>
    public bool ShotOn;
    public double Sx, Sy, Svx, Svy;

    public CannonState Cannon;
    public double Cx, Cy;

    /// <summary>The Destroyer Missile, always coming after the Yar.</summary>
    public double Mx, My;

    public SwirlState Swirl;
    /// <summary>Seconds until the Qotile turns into the Swirl, or until the Swirl leaves its pad.</summary>
    public double SwirlTime;
    public double SwX, SwY, SwVx, SwVy;

    /// <summary>The Qotile (or Swirl) destroyed: the explosion, and how far into it.</summary>
    public bool Destroyed;
    public double BoomTime, BoomX, BoomY;

    /// <summary>Seconds before the Yar can take its next bite.</summary>
    public double EatWait;
    public bool FireHeld;

    public int Score, Bonus, Eaten, Shot, Qotiles;
    public double Clock;
    public Dice Dice = new(1);
    public List<Sfx> Heard = [];

    public World Copy()
    {
        var c = (World)MemberwiseClone();
        c.Cells = (bool[,])Cells.Clone();
        c.Heard = [];
        return c;
    }

    public double QotileX => Shield.X + Shield.QotileDx;
    public double QotileY => ShieldY + Shield.QotileDy;
    public bool InZone => X > Field.ZoneLeft && X < Field.ZoneRight;
    public int CellsLeft
    {
        get
        {
            int n = 0;
            foreach (bool b in Cells) if (b) n++;
            return n;
        }
    }

    public double CellLeft(int c) => Shield.X + c * Shield.CellW;
    public double CellTop(int r) => ShieldY + r * Shield.CellH;
}
