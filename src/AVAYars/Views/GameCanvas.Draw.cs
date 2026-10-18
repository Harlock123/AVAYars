using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAYars.Engine;

namespace AVAYars.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAYars/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;
    /// <summary>The Yar is drawn a little larger than the other sprites' pixels.</summary>
    private const double YarPixel = 5;
    private const uint YarBody = 0xFFD8E85A, YarWing = 0xFF5ACC5A, Ground = 0xFF04040A;

    /// <summary>Colours in the spirit of the 2600's palette, for the Neutral Zone, the Qotile and the explosion.</summary>
    private static readonly uint[] Palette =
    [
        0xFFE04A3A, 0xFFF08A2A, 0xFFF4D040, 0xFF8ADC3A, 0xFF3AC86A, 0xFF2AC8C8, 0xFF3A8AF0, 0xFF6A5AF0,
        0xFFB04AE0, 0xFFE04AB0, 0xFFF0F0F0, 0xFF8A8AA0,
    ];

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Shade(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    // ───────────────────────────── sprites ─────────────────────────────

    /// <summary>
    /// Draws a sprite of chunky pixels centred on a point: '#' in the main colour, 'o' in the second; with an outline
    /// in black if asked, so it stands out against the Neutral Zone.
    /// </summary>
    private static void Sprite(DrawingContext dc, string[] rows, double cx, double cy, uint main, uint second = 0, double px = Field.Pixel, double outline = 0)
    {
        double x0 = Math.Round(cx - rows[0].Length * px / 2), y0 = Math.Round(cy - rows.Length * px / 2);
        if (outline > 0)
            for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] != '.')
                    dc.FillRectangle(Brush(0xFF000000), new Rect(x0 + c * px - outline, y0 + r * px - outline, px + 2 * outline, px + 2 * outline));
        for (int r = 0; r < rows.Length; r++)
        for (int c = 0; c < rows[r].Length; c++)
        {
            char ch = rows[r][c];
            if (ch == '.') continue;
            dc.FillRectangle(Brush(ch == 'o' && second != 0 ? second : main), new Rect(x0 + c * px, y0 + r * px, px, px));
        }
    }

    /// <summary>The Yar facing up, and facing up and right, wings out and wings in; the other ways are turned from these.</summary>
    private static readonly string[][] YarUp =
    [
        [
            "...##...",
            "o..##..o",
            "oo####oo",
            "ooo##ooo",
            "ooo##ooo",
            ".o.##.o.",
            "...##...",
            "..#..#..",
        ],
        [
            "...##...",
            "...##...",
            "..####..",
            ".oo##oo.",
            ".oo##oo.",
            "..o##o..",
            "...##...",
            "..#..#..",
        ],
    ];

    private static readonly string[][] YarUpRight =
    [
        [
            "ooo...##",
            "oooo.###",
            "oooo###.",
            ".oo###..",
            "..###oo.",
            ".###oooo",
            "#.#.oooo",
            ".....ooo",
        ],
        [
            "......##",
            "..o..###",
            ".ooo###.",
            "..o###..",
            "..###o..",
            ".###ooo.",
            "#.#..o..",
            "........",
        ],
    ];

    /// <summary>[way][frame]: the ways clockwise from up (0) round to up-left (7).</summary>
    private static readonly string[][][] YarSprites = MakeYar();

    private static string[][][] MakeYar()
    {
        var all = new string[8][][];
        for (int way = 0; way < 8; way++)
        {
            all[way] = new string[2][];
            for (int f = 0; f < 2; f++)
            {
                var s = way % 2 == 0 ? YarUp[f] : YarUpRight[f];
                for (int k = 0; k < way / 2; k++) s = Turn90(s);
                all[way][f] = s;
            }
        }
        return all;
    }

    /// <summary>A sprite turned a quarter of the way round, clockwise.</summary>
    private static string[] Turn90(string[] s)
    {
        int n = s.Length;
        return Enumerable.Range(0, n).Select(r => new string(Enumerable.Range(0, n).Select(c => s[n - 1 - c][r]).ToArray())).ToArray();
    }

    private static int Way(int fx, int fy) => (fx, fy) switch
    {
        (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => 2,
    };

    private static readonly string[] Qotile =
    [
        "..######..",
        ".########.",
        "##oo##oo##",
        "##oo##oo##",
        "##########",
        "#o#o##o#o#",
        "##########",
        ".#.####.#.",
        "#..#..#..#",
        "..#....#..",
    ];

    private static readonly string[] Cannon =
    [
        "###...",
        "######",
        "######",
        "###...",
    ];

    private static void DrawYar(DrawingContext dc, double x, double y, int way, bool flap, double px = YarPixel)
    {
        var s = YarSprites[way][flap ? 1 : 0];
        Sprite(dc, s, x, y, YarBody, YarWing, px, outline: px > 3 ? 2 : 0);
    }

    /// <summary>The Swirl: a spiral of chunky pixels, spinning, flashing through the colours.</summary>
    private void DrawSwirl(DrawingContext dc, double x, double y, double spin)
    {
        const int n = 11;
        double px = Field.Pixel, x0 = Math.Round(x - n * px / 2), y0 = Math.Round(y - n * px / 2);
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
        {
            double dx = c - 5, dy = r - 5, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 5.4) continue;
            double a = Math.Atan2(dy, dx);
            if (d > 0.5 && Math.Sin(2 * a + d * 1.1 - spin) < 0.05) continue;
            uint colour = Palette[(int)(_clock * 20 + d) % 10];
            dc.FillRectangle(Brush(colour), new Rect(x0 + c * px, y0 + r * px, px, px));
        }
    }

    private static uint CellColour(Shape shape, int r)
    {
        double t = Math.Abs(r - (Shield.Rows - 1) / 2.0) / ((Shield.Rows - 1) / 2.0);
        return shape == Shape.Arch ? Mix(0xFFF4B030, 0xFFC8341C, t) : Mix(0xFFF07AC8, 0xFF8A3AD8, t);
    }

    // ───────────────────────────── the screen ─────────────────────────────

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawPlay(dc, g.World, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawPlay(dc, g.World, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawPlay(dc, g.World, true); DrawTop(dc); DrawGameOver(dc); break;
            default:
                DrawPlay(dc, g.World, true);
                DrawTop(dc);
                if (g.Message is { } msg)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(0, 372, Field.Width, 64));
                    Text(dc, msg, new Point(512, 404), 46, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 92), 22, Yellow, center: true);
    }

    /// <summary>The Neutral Zone: a band of colour that never sits still.</summary>
    private void DrawZone(DrawingContext dc, double top, double bottom)
    {
        int frame = (int)(_clock * 30);
        const double w = 8, h = 4;
        for (double y = top; y < bottom; y += h)
        for (double x = Field.ZoneLeft; x < Field.ZoneRight; x += w)
        {
            uint hsh = Hash((int)(x / w), (int)(y / h), frame);
            if (hsh % 2 == 0) continue;
            uint colour = Palette[(hsh >> 8) % 10];
            dc.FillRectangle(Brush(Shade(colour, 0.32 + (hsh >> 16) % 30 / 100.0)), new Rect(x, y, w, h));
        }
    }

    private void DrawPlay(DrawingContext dc, World w, bool movers)
    {
        var g = Engine;
        dc.FillRectangle(Brush(Ground), new Rect(0, 0, Field.Width, Field.Height));
        bool boom = movers && w.Destroyed;
        double shake = boom ? Math.Max(0, 1 - w.BoomTime / 2.0) * 10 : 0;
        double ox = shake > 0 ? ((int)(Hash((int)(_clock * 40), 1, 9) % 21) - 10) / 10.0 * shake : 0;
        double oy = shake > 0 ? ((int)(Hash((int)(_clock * 40), 2, 9) % 21) - 10) / 10.0 * shake : 0;
        using var clip = dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.PlayHeight));
        using var push = dc.PushTransform(Matrix.CreateTranslation(ox, oy));
        DrawZone(dc, Field.Top - 16, Field.Bottom + 16);
        if (!w.Destroyed || !movers)
        {
            // The shield.
            for (int r = 0; r < Shield.Rows; r++)
            {
                uint colour = CellColour(w.Shape, r);
                for (int c = 0; c < Shield.Cols; c++)
                {
                    if (!w.Cells[r, c]) continue;
                    double x = w.CellLeft(c), y = w.CellTop(r);
                    dc.FillRectangle(Brush(colour), new Rect(x + 1, y + 1, Shield.CellW - 2, Shield.CellH - 2));
                    dc.FillRectangle(Brush(Shade(colour, 0.7)), new Rect(x + 1, y + Shield.CellH - 5, Shield.CellW - 2, 4));
                }
            }
            // The Qotile, or the Swirl on its pad.
            if (w.Swirl == SwirlState.Home)
                Sprite(dc, Qotile, w.QotileX, w.QotileY, Palette[(int)(_clock * 3) % 10], 0xFF000000);
            else if (w.Swirl == SwirlState.Pad)
                DrawSwirl(dc, w.QotileX, w.QotileY, _clock * 14);
        }
        if (!movers) { Scanlines(dc); return; }
        if (w.Destroyed) DrawBoom(dc, w);
        else
        {
            if (w.Swirl == SwirlState.Flying) DrawSwirl(dc, w.SwX, w.SwY, _clock * 14);
            // The Destroyer Missile, flickering.
            dc.FillRectangle(Brush((int)(_clock * 20) % 2 == 0 ? White : Orange), new Rect(Math.Round(w.Mx - 4), Math.Round(w.My - 4), 8, 8));
            if (w.Cannon != CannonState.None)
            {
                uint cc = new[] { Yellow, White, Orange }[(int)(_clock * 12) % 3];
                Sprite(dc, Cannon, w.Cx, w.Cy, cc);
                if (w.Cannon == CannonState.Fired)
                    for (int k = 1; k <= 4; k++)
                        dc.FillRectangle(Brush(Alpha(cc, 0.6 - k * 0.12)), new Rect(w.Cx - 12 - k * 12, w.Cy - 4, 10, 8));
            }
        }
        if (w.ShotOn) dc.FillRectangle(Brush(YarBody), new Rect(Math.Round(w.Sx - 4), Math.Round(w.Sy - 4), 8, 8));
        // The Yar, drawn again across the wrap when it's at the top or bottom.
        bool dying = g.Phase == Phase.Dying && g.World == w;
        bool gone = dying && g.State == GameState.GameOver;
        if (!gone && !(dying && (int)(g.PhaseTime * 10) % 3 == 0))
        {
            int way = dying ? (int)(g.PhaseTime * 16) % 8 : Way(w.Fx, w.Fy);
            bool flap = (int)(_clock * (w.Moving ? 18 : 8)) % 2 == 0;
            foreach (double dy in new[] { 0, -Field.PlayHeight, Field.PlayHeight })
                if (w.Y + dy is > Field.Top - 20 and < Field.Bottom + 20) DrawYar(dc, w.X, w.Y + dy, way, flap);
        }
        Scanlines(dc);
    }

    /// <summary>The Qotile gone: the screen torn into stripes of colour, and a ring of fire spreading from where it was.</summary>
    private void DrawBoom(DrawingContext dc, World w)
    {
        double t = w.BoomTime;
        int frame = (int)(_clock * 30);
        double fade = Math.Clamp(1.4 - t / 2.5, 0, 1);
        for (double y = Field.Top - 16; y < Field.Bottom + 16; y += 4)
        {
            uint h = Hash((int)(y / 4), frame, 3);
            if (h % 3 == 0) continue;
            double x0 = h % 200 - 100 + (t * 900 * ((h >> 9) % 3 + 1)) % 1024;
            uint colour = Palette[(h >> 4) % 10];
            dc.FillRectangle(Brush(Alpha(colour, fade * 0.8)), new Rect(-200, y, Field.Width + 400, 4));
            dc.FillRectangle(Brush(Alpha(0xFF000000, fade * 0.5)), new Rect(x0, y, 120 + h % 240, 4));
        }
        double radius = t * 420;
        for (int k = 0; k < 48; k++)
        {
            double a = k * Math.PI * 2 / 48 + t, rr = radius * (0.8 + Hash(k, 7, 1) % 40 / 100.0);
            double x = w.BoomX + Math.Cos(a) * rr, y = w.BoomY + Math.Sin(a) * rr;
            dc.FillRectangle(Brush(Alpha(Palette[(k + frame) % 10], 1 - t / 3)), new Rect(Math.Round(x / 4) * 4, Math.Round(y / 4) * 4, 8, 8));
        }
    }

    /// <summary>Faint lines across, like a television's.</summary>
    private static void Scanlines(DrawingContext dc)
    {
        var b = Brush(0x30000000);
        for (double y = Field.Top; y < Field.Bottom; y += 4) dc.FillRectangle(b, new Rect(0, y + 3, Field.Width, 1));
    }

    private void DrawTop(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF101020), new Rect(0, Field.Top - 2, Field.Width, 2));
        dc.FillRectangle(Brush(0xFF101020), new Rect(0, Field.Bottom, Field.Width, 2));
        Text(dc, $"SCORE {g.Score}", new Point(24, 32), 34, White);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, g.Score)}", new Point(512, 32), 30, Dim, center: true);
        Text(dc, $"LEVEL {w.Level}", new Point(1000, 32), 34, Yellow, right: true);
        // The Yars to come, along the bottom.
        for (int i = 0; i < Math.Min(g.Lives - 1, 8); i++) DrawYar(dc, 24 + i * 26, 756, 0, false, 2.5);
        if (w.Cannon == CannonState.Ready && (int)(_clock * 4) % 2 == 0)
            Text(dc, "ZORLON CANNON READY", new Point(1000, 756), 22, Yellow, right: true);
        else if (w.InZone && !w.Destroyed)
            Text(dc, "NEUTRAL ZONE", new Point(1000, 756), 22, Cyan, right: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // The playfield behind the title: the shield, the Qotile and the zone.
        var demo = new World();
        Sim.StartLevel(demo, 1);
        demo.ShieldY = Field.Top + (Field.PlayHeight - Shield.Height) / 2 + Math.Sin(_clock * 0.8) * 120;
        DrawPlay(dc, demo, false);
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 440, 96, 880, 520), 12);
        string title = "AVA YARS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64, ty = 170 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 116, i < 3 ? YarBody : Palette[(i + (int)(_clock * 2)) % 10], center: true);
        }
        DrawYar(dc, cx - 330, 170, 2, (int)(_clock * 12) % 2 == 0, 6);
        Sprite(dc, Qotile, cx + 330, 170, Palette[(int)(_clock * 3) % 10], 0xFF000000, 6);
        Text(dc, "AFTER HOWARD SCOTT WARSHAW'S 1982 YARS' REVENGE FOR THE ATARI 2600", new Point(cx, 240), 25, White, center: true);
        Text(dc, "EAT OR SHOOT YOUR WAY THROUGH THE SHIELD. A BITE CALLS THE ZORLON CANNON: FIRE IT", new Point(cx, 286), 22, White, center: true);
        Text(dc, "ACROSS AT THE QOTILE, AND GET OUT OF ITS WAY. THE DESTROYER MISSILE NEVER STOPS COMING;", new Point(cx, 312), 22, White, center: true);
        Text(dc, "IN THE NEUTRAL ZONE IT CAN'T HURT YOU, BUT YOU CAN'T FIRE. LOOK OUT FOR THE SWIRL.", new Point(cx, 338), 22, White, center: true);
        // The scores.
        double y1 = 396, y2 = 446;
        dc.FillRectangle(Brush(CellColour(Shape.Arch, 4)), new Rect(cx - 387, y1 - 10, 14, 18));
        Text(dc, "CELL SHOT 69, EATEN 169", new Point(cx - 340, y1), 26, Orange);
        Sprite(dc, Qotile, cx + 76, y1, Palette[(int)(_clock * 3) % 10], 0xFF000000, 2.5);
        Text(dc, "QOTILE 1000", new Point(cx + 110, y1), 26, Orange);
        DrawSwirl(dc, cx - 380, y2, _clock * 14);
        Text(dc, "SWIRL ON ITS PAD 2000", new Point(cx - 340, y2), 26, Orange);
        DrawSwirl(dc, cx + 76, y2, -_clock * 14);
        Text(dc, "IN FLIGHT 6000 AND A YAR", new Point(cx + 110, y2), 26, Orange);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 556), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD8000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "ARROWS OR WASD: FLY (TWO AT ONCE FOR THE DIAGONALS)   SPACE, Z OR X: FIRE   P: PAUSE   M: SOUND", new Point(cx, 718), 19, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: FLIGHT OF THE BUMBLEBEE (RIMSKY-KORSAKOV, 1900)", new Point(cx, 746), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 380, 250, 760, 240), 12);
        Text(dc, "GAME OVER", new Point(cx, 320), 74, (int)(_clock * 4) % 2 == 0 ? Yellow : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 396), 44, White, center: true);
        int q = g.World.Qotiles;
        Text(dc, $"LEVEL {g.Level}, {q} QOTILE{(q == 1 ? "" : "S")} DESTROYED", new Point(cx, 448), 28, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BRAVEST YARS", new Point(cx, 120), 54, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  LEVEL", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
