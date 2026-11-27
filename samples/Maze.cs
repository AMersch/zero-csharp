// Example and test: a walk through a maze, drawn by a raycaster as in the first
// 3D games, and walking by itself as a screen saver does. At every junction it
// picks a way at random, in a dead end it turns round, and any key ends it.
//
// For each of 160 columns of the picture a ray goes out from the eye, through
// the 16 by 16 squares of the map, to the first wall: the nearer the wall, the
// taller the column. Bitmap.Columns draws all 160 at once, ceiling, wall and floor.
// The picture is 160 by 120 pixels, which the video chip shows over the whole
// screen. Positions are in 256ths of a square and angles in 1024ths of a turn,
// all whole numbers; the video chip multiplies (CX16.Multiply). The ray's loop
// counts in bytes and 16 bits: C#'s 32-bit numbers are slow on this processor.
//
// The checks try the drawing, the walk and the view. Exit code = number of
// failed checks.
// Build: ./build.sh --cx16 Maze    Run: ./run.sh --cx16 --window Maze

using System;
using System.Runtime.CompilerServices;

public static class Program
{
    const int Rays = 160, ViewHeight = 120;
    const int Ceiling = 6, Floor = 11;                  // blue, dark gray
    const int WallAlong = 5, WallAcross = 13;           // green and light green: corners show

    // '#' is a wall. The walk needs walls all round the edge.
    static readonly string[] s_rows = new string[]
    {
        "################",
        "#.....#.#.....##",
        "###.#.#.#.#.####",
        "#...#.#...#...##",
        "###.#.###.###.##",
        "#...#...#...#.##",
        "#.#.###.###.#.##",
        "#.#...#.......##",
        "#.#.#####.#.#.##",
        "#.....#.......##",
        "#.#.#.#.#####.##",
        "#...#.#.......##",
        "#.###.#.#.###.##",
        "#...#.....#...##",
        "################",
        "################",
    };

    static readonly byte[] s_map = new byte[256];       // 1 is a wall
    static readonly short[] s_cos = new short[1024];    // 256 is 1
    static readonly ushort[] s_delta = new ushort[1024];    // a ray's length from one grid line to the next
    static readonly byte[] s_height = new byte[768];    // a wall's height at a distance, in 32nds of a square

    // The way the walk goes: 0 east, 1 south, 2 west, 3 north, as the angle / 256.
    static int s_cell, s_way, s_progress, s_turn, s_angle;
    static ushort s_random = 1;

    // The picture to draw: each column's wall from which row to which, and its color.
    static readonly byte[] s_top = new byte[Rays];
    static readonly byte[] s_bottom = new byte[Rays];
    static readonly byte[] s_color = new byte[Rays];

    static int s_checks, s_failures;
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok && s_failures++ == 0)
            s_firstFailure = name;
    }

    // ---- the tables -------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Prepare()
    {
        for (int cell = 0; cell < 256; cell++)
            s_map[cell] = (byte)(s_rows[cell / 16][cell % 16] == '#' ? 1 : 0);
        // CX16.Cos knows 256 angles of a turn; between them, a straight line will do.
        for (int a = 0; a < 1024; a++)
        {
            int low = CX16.Cos(a >> 2), high = CX16.Cos((a >> 2) + 1);
            int cos = (low + (high - low) * (a & 3) / 4 + 64) >> 7;
            s_cos[a] = (short)cos;
            int size = cos < 0 ? -cos : cos;
            s_delta[a] = (ushort)(size < 9 ? 8000 : 65536 / size);
        }
        // A wall one square high and one away takes 0.9 of the view.
        for (int i = 0; i < 768; i++)
            s_height[i] = (byte)Math.Min(ViewHeight, 6912 / (2 * i + 1));
    }

    // ---- the walk ---------------------------------------------------------------------

    static int Random(int below)
    {
        s_random ^= (ushort)(s_random << 7);
        s_random ^= (ushort)(s_random >> 9);
        s_random ^= (ushort)(s_random << 8);
        return s_random % below;
    }

    static int Next(int cell, int way) => cell + (way == 0 ? 1 : way == 1 ? 16 : way == 2 ? -1 : -16);

    static bool Open(int cell, int way) => s_map[Next(cell, way)] == 0;

    static void Start(int cell, int seed)
    {
        s_random = (ushort)(seed == 0 ? 1 : seed);
        s_cell = cell;
        s_way = 0;
        while (!Open(cell, s_way))
            s_way++;
        s_angle = s_way * 256;
        s_progress = 0;
        s_turn = 0;
    }

    // In the middle of a square: on to which one? Any open way but back; back only
    // where there is no other.
    static void Decide()
    {
        int back = (s_way + 2) & 3;
        int count = 0;
        for (int way = 0; way < 4; way++)
            if (way != back && Open(s_cell, way))
                count++;
        int chosen = back;
        if (count > 0)
        {
            int pick = Random(count);
            for (int way = 0; way < 4; way++)
                if (way != back && Open(s_cell, way) && pick-- == 0)
                    chosen = way;
        }
        int quarters = (chosen - s_way) & 3;
        s_turn = quarters == 1 ? 256 : quarters == 3 ? -256 : quarters == 2 ? 512 : 0;
        s_way = chosen;
    }

    // Walks or turns by units of 256ths of a square; a turn goes at twice that.
    // Returns true on arriving in a square.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Advance(int units)
    {
        if (s_turn != 0)
        {
            int step = Math.Min(units * 2, s_turn < 0 ? -s_turn : s_turn);
            if (s_turn < 0)
                step = -step;
            s_angle = (s_angle + step) & 1023;
            s_turn -= step;
            return false;
        }
        s_progress += units;
        if (s_progress < 256)
            return false;
        s_progress = 0;
        s_cell = Next(s_cell, s_way);
        Decide();
        return true;
    }

    // ---- the view ---------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Render()
    {
        // The eye: the middle of the square, and so far towards the next one.
        int along = s_turn != 0 ? 0 : s_progress;
        int x = (s_cell & 15) * 256 + 128 + (s_way == 0 ? along : s_way == 2 ? -along : 0);
        int y = (s_cell >> 4) * 256 + 128 + (s_way == 1 ? along : s_way == 3 ? -along : 0);
        byte start = (byte)((y >> 8) * 16 + (x >> 8));
        short west = (short)(x & 255), east = (short)(256 - west);
        short north = (short)(y & 255), south = (short)(256 - north);

        byte[] map = s_map;
        short[] cos = s_cos;
        ushort[] delta = s_delta;
        int a = (s_angle - Rays / 2) & 1023;
        for (int column = 0; column < Rays; column++)
        {
            int b = (a - 256) & 1023;               // the sine is the cosine a quarter turn on
            ushort dx = delta[a], dy = delta[b];
            byte stepX = 1, stepY = 16;             // added to a byte, 255 goes one back
            short fx = east, fy = south;
            if (cos[a] < 0)
            {
                stepX = 255;
                fx = west;
            }
            if (cos[b] < 0)
            {
                stepY = 240;
                fy = north;
            }
            // How far along the ray the next grid line across and down is.
            ushort sx = (ushort)(CX16.Multiply(fx, (short)dx) >> 8);
            ushort sy = (ushort)(CX16.Multiply(fy, (short)dy) >> 8);
            byte cell = start;
            ushort distance;
            byte color;
            while (true)
            {
                if (sx < sy)
                {
                    cell += stepX;
                    if (map[cell] != 0)
                    {
                        distance = sx;
                        color = WallAcross;
                        break;
                    }
                    sx += dx;
                }
                else
                {
                    cell += stepY;
                    if (map[cell] != 0)
                    {
                        distance = sy;
                        color = WallAlong;
                        break;
                    }
                    sy += dy;
                }
            }
            // The distance straight ahead, not along the ray: or walls would bulge.
            int ahead = CX16.Multiply((short)distance, cos[(column - Rays / 2) & 1023]) >> 11;
            byte height = s_height[ahead < 767 ? ahead : 767];
            byte top = (byte)((ViewHeight - height) >> 1);
            s_top[column] = top;
            s_bottom[column] = (byte)(top + height);
            s_color[column] = color;
            a = (a + 1) & 1023;
        }
        Bitmap.Columns(0, ViewHeight, s_top, s_bottom, s_color, Ceiling, Floor);
    }

    static int s_video;         // which layers were on, before

    // 160 by 120 pixels over the whole screen, and no text in front.
    static void ShowView()
    {
        CX16.Poke(0x9F25, 0);
        s_video = CX16.Peek(0x9F29);
        Bitmap.Enable();
        Bitmap.Clear(0);
        CX16.Poke(0x9F2A, 32);                          // a quarter of 640 across
        CX16.Poke(0x9F2B, 32);                          // and of 480 down
        CX16.Poke(0x9F29, CX16.Peek(0x9F29) & ~0x20);   // the text's layer off
    }

    // ---- the checks -------------------------------------------------------------------

    static bool Pixels(int x, int y, int color, int times)
    {
        for (int i = 0; i < times; i++)
            if (Bitmap.Get(x, y + i) != color)
                return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CheckColumns()
    {
        Bitmap.Clear(0);
        Span<byte> first = stackalloc byte[3];
        Span<byte> second = stackalloc byte[3];
        Span<byte> color = stackalloc byte[3];
        first[0] = 3;
        second[0] = 7;
        color[0] = 2;
        first[1] = 0;
        second[1] = 12;
        color[1] = 4;
        Bitmap.Columns(10, 12, first.Slice(0, 2), second.Slice(0, 2), color.Slice(0, 2), 1, 3);
        Check("above, middle and below", Pixels(10, 0, 1, 3) && Pixels(10, 3, 2, 4) && Pixels(10, 7, 3, 5));
        Check("each column as it says", Pixels(11, 0, 4, 12));
        Check("and no wider or taller", Bitmap.Get(9, 5) == 0 && Bitmap.Get(12, 5) == 0 && Bitmap.Get(10, 12) == 0);
        for (int i = 0; i < 3; i++)
        {
            first[i] = 0;
            second[i] = 4;
            color[i] = (byte)(5 + i);
        }
        Bitmap.Columns(-1, 4, first, second, color, 1, 3);
        Check("cut off at the left", Pixels(0, 0, 6, 4) && Pixels(1, 0, 7, 4) && Bitmap.Get(2, 0) == 0
            && Bitmap.Get(319, 0) == 0);
        Bitmap.Columns(319, 4, first, second, color, 1, 3);
        Check("cut off at the right", Pixels(319, 0, 5, 4) && Bitmap.Get(0, 4) == 0);
        second[0] = 250;
        Bitmap.Columns(20, 500, first.Slice(0, 1), second.Slice(0, 1), color.Slice(0, 1), 1, 3);
        Check("a picture's height at most", Pixels(20, 0, 5, 240));
        first[0] = 8;
        second[0] = 2;
        Bitmap.Columns(21, 6, first.Slice(0, 1), second.Slice(0, 1), color.Slice(0, 1), 1, 3);
        Check("an end above the start", Pixels(21, 0, 1, 6) && Bitmap.Get(21, 6) == 0);
        Bitmap.Columns(22, 4, first.Slice(0, 0), second, color, 1, 3);
        Bitmap.Columns(23, 0, first, second, color, 1, 3);
        Check("nothing of no width or height", Bitmap.Get(22, 0) == 0 && Bitmap.Get(23, 0) == 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CheckWalk()
    {
        byte[] visited = new byte[256];
        bool outOfWalls = true, backOnlyInDeadEnds = true;
        int deadEnds = 0, junctions = 0, straight = 0, sideways = 0;
        Start(17, 1234);
        for (int arrivals = 0; arrivals < 400;)
        {
            int before = s_way;
            if (!Advance(64))
                continue;
            arrivals++;
            visited[s_cell] = 1;
            outOfWalls &= s_map[s_cell] == 0;
            int ways = 0;
            for (int way = 0; way < 4; way++)
                if (Open(s_cell, way))
                    ways++;
            bool back = s_way == ((before + 2) & 3);
            backOnlyInDeadEnds &= back == (ways == 1);
            if (ways == 1)
                deadEnds++;
            if (ways > 2)
            {
                junctions++;
                if (s_way == before)
                    straight++;
                else
                    sideways++;
            }
        }
        int cells = 0;
        for (int cell = 0; cell < 256; cell++)
            cells += visited[cell];
        Check("never in a wall", outOfWalls);
        Check("back only in a dead end, and there always", backOnlyInDeadEnds && deadEnds > 0);
        Check("at a junction now straight on, now not", junctions > 10 && straight > 0 && sideways > 0);
        Check("it gets around", cells >= 50);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CheckView()
    {
        // From the first square east along a corridor, which ends 4.5 squares on.
        Start(17, 1);
        Render();
        Check("far off: ceiling, wall, floor", Bitmap.Get(80, 0) == Ceiling && Bitmap.Get(80, 60) == WallAcross
            && Bitmap.Get(80, 119) == Floor && Bitmap.Get(80, 30) == Ceiling && Bitmap.Get(80, 90) == Floor);
        int top = 0;
        while (top < 60 && Bitmap.Get(80, top) == Ceiling)
            top++;
        Check("as tall as its distance says", top >= 47 && top <= 49);
        Check("a wall ahead is level", Bitmap.Get(74, top) == WallAcross && Bitmap.Get(86, top) == WallAcross
            && Bitmap.Get(74, top - 1) == Ceiling && Bitmap.Get(86, top - 1) == Ceiling);
        // Turned south, where a wall is half a square away: it fills the view.
        s_angle = 256;
        Render();
        Check("near: all wall", Bitmap.Get(80, 0) == WallAlong && Bitmap.Get(80, 119) == WallAlong);
    }

    public static int Main()
    {
        Prepare();
        ShowView();
        CheckColumns();
        CheckWalk();
        CheckView();

        // The walk: so many units a jiffy, whatever a picture takes to draw.
        Start(17, CX16.Entropy);
        uint started = CX16.Ticks, last = started;
        int pictures = 0;
        do
        {
            Render();
            pictures++;
            uint now = CX16.Ticks;
            Advance((int)Math.Min(now - last, 12u) * 4);
            last = now;
        }
        while (CX16.ReadKey() == 0);
        int jiffies = (int)(last - started);

        // The mode puts the zoom back, but not the text's layer, which was switched off.
        CX16.SetMode(CX16.Text80x60);
        CX16.Poke(0x9F25, 0);
        CX16.Poke(0x9F29, s_video);
        Check("the screen is as it was", CX16.Peek(0x9F2A) == 128 && CX16.Peek(0x9F2B) == 128
            && (CX16.Peek(0x9F29) & 0x20) != 0);
        CX16.Clear(CX16.Blue);
        // (Not in the headless test, which ends at once.)
        if (pictures >= 10 && jiffies > 0)
        {
            int tenths = pictures * 600 / jiffies;
            Out.Line((tenths / 10).ToString() + "." + (tenths % 10).ToString() + " pictures a second");
        }
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
