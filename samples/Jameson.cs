// Example and test: a wireframe spaceship in the manner of the title screens of the 1980s. It grows out of a
// point in the middle of the picture while it turns about all three axes, and goes on turning until a key is
// pressed. The ship is a design of this sample's own, a flat wedge; it is no ship out of any game.
//
// The lines that are hidden are not drawn. The ship is convex (no part of it can be in front of another), so
// that is simple: a face shows if its corners go round the right way on the screen, and an edge is drawn if
// one of its two faces shows. Rotation and perspective are whole numbers throughout, with the video chip's
// multiplier (CX16.Multiply); the lines are its line helper's (Bitmap.Line). The video memory has no room for
// a second picture, so the picture on show is changed, one edge at a time: the old line is drawn over in black
// and the new one at once in white, so that only one line is ever missing. Then all are drawn once more, which
// closes the gaps where erasing one line cut another. (Erasing them all first flickers badly: half the
// frames showed an unfinished ship.)
//
// The checks try the ship's shape, the projection, the hidden lines and the drawing. Exit code = number of
// failed checks.
// Build: ./build.sh --cx16 Jameson    Run: ./run.sh --cx16 --window Jameson

using System;

public static class Program
{
    const int Corners = 8, Edges = 17, Faces = 11;
    const int Focal = 256, Distance = 360, Grow = 64;       // the eye to the screen, and to the ship at the end; frames of growing

    // The corners, x to the right, y up, z away from the eye: nose, wing tips, the top of the cockpit, the stern.
    static readonly short[] s_x = new short[] { 0, -88, 88, 0, -32, 32, -32, 32 };
    static readonly short[] s_y = new short[] { -2, -6, -6, 28, 14, 14, -18, -18 };
    static readonly short[] s_z = new short[] { 96, -36, -36, -12, -52, -52, -52, -52 };

    // Three corners of each face, counterclockwise as seen from outside (the stern, the last, has four).
    static readonly byte[] s_faces = new byte[]
    {
        1, 0, 3,   6, 0, 1,   3, 0, 2,   2, 0, 7,   7, 0, 6,   4, 1, 3,   6, 1, 4,   3, 2, 5,   5, 2, 7,   4, 3, 5,   6, 4, 5,
    };

    // Each edge: its two corners and the two faces that meet there.
    static readonly byte[] s_edges = new byte[]
    {
        0, 1, 0, 1,    0, 2, 2, 3,    0, 3, 0, 2,    0, 6, 1, 4,    0, 7, 3, 4,    1, 3, 0, 5,
        1, 4, 5, 6,    1, 6, 1, 6,    2, 3, 2, 7,    2, 5, 7, 8,    2, 7, 3, 8,    3, 4, 5, 9,
        3, 5, 7, 9,    4, 5, 9, 10,   4, 6, 6, 10,   5, 7, 8, 10,   6, 7, 4, 10,
    };

    static readonly short[] s_m = new short[9];             // the rotation, 16384 is 1
    static readonly int[] s_rx = new int[Corners], s_ry = new int[Corners], s_rz = new int[Corners];     // turned, and moved away
    static readonly int[] s_sx = new int[Corners], s_sy = new int[Corners];                              // on the screen
    static readonly bool[] s_shows = new bool[Faces];

    // What is on the screen now: for each edge whether it is, and the ends of its line, to be drawn over.
    static readonly bool[] s_on = new bool[Edges];
    static readonly short[] s_drawn = new short[Edges * 4];
    static int s_lines;

    static short Times(short a, short b) => (short)(CX16.Multiply(a, b) >> 14);

    // The matrix for turning by a about the x axis, then by b about y, then by c about z (angles in 256ths).
    static void Turn(int a, int b, int c)
    {
        short sa = (short)(CX16.Sin(a) >> 1), ca = (short)(CX16.Cos(a) >> 1);
        short sb = (short)(CX16.Sin(b) >> 1), cb = (short)(CX16.Cos(b) >> 1);
        short sc = (short)(CX16.Sin(c) >> 1), cc = (short)(CX16.Cos(c) >> 1);
        short sbsa = Times(sb, sa), sbca = Times(sb, ca);
        s_m[0] = Times(cc, cb);
        s_m[1] = (short)(Times(cc, sbsa) - Times(sc, ca));
        s_m[2] = (short)(Times(cc, sbca) + Times(sc, sa));
        s_m[3] = Times(sc, cb);
        s_m[4] = (short)(Times(sc, sbsa) + Times(cc, ca));
        s_m[5] = (short)(Times(sc, sbca) - Times(cc, sa));
        s_m[6] = (short)-sb;
        s_m[7] = Times(cb, sa);
        s_m[8] = Times(cb, ca);
    }

    static int Row(int row, short x, short y, short z)
        => (CX16.Multiply(s_m[row], x) + CX16.Multiply(s_m[row + 1], y) + CX16.Multiply(s_m[row + 2], z) + 8192) >> 14;

    // Where every corner is on the screen, and which faces show, for the ship `distance` away.
    static void Project(int distance)
    {
        for (int i = 0; i < Corners; i++)
        {
            int x = Row(0, s_x[i], s_y[i], s_z[i]), y = Row(3, s_x[i], s_y[i], s_z[i]), z = Row(6, s_x[i], s_y[i], s_z[i]) + distance;
            s_rx[i] = x;
            s_ry[i] = y;
            s_rz[i] = z;
            s_sx[i] = 160 + x * Focal / z;
            s_sy[i] = 120 - y * Focal / z;
        }
        for (int f = 0; f < Faces; f++)
        {
            int p = s_faces[3 * f], q = s_faces[3 * f + 1], r = s_faces[3 * f + 2];
            s_shows[f] = CX16.Multiply((short)(s_sx[q] - s_sx[p]), (short)(s_sy[r] - s_sy[p]))
                       > CX16.Multiply((short)(s_sy[q] - s_sy[p]), (short)(s_sx[r] - s_sx[p]));
        }
    }

    static bool EdgeShows(int edge) => s_shows[s_edges[4 * edge + 2]] || s_shows[s_edges[4 * edge + 3]];

    static void Line(int edge, int color)
    {
        int i = 4 * edge;
        Bitmap.Line(s_drawn[i], s_drawn[i + 1], s_drawn[i + 2], s_drawn[i + 3], color);
    }

    // Takes the ship off the screen.
    static void Erase()
    {
        for (int e = 0; e < Edges; e++)
            if (s_on[e])
            {
                Line(e, 0);
                s_on[e] = false;
            }
        s_lines = 0;
    }

    // Shows the ship as Project left it, in place of what is there.
    static void Draw()
    {
        s_lines = 0;
        for (int e = 0; e < Edges; e++)
        {
            if (s_on[e])
                Line(e, 0);
            s_on[e] = EdgeShows(e);
            if (!s_on[e])
                continue;
            int p = s_edges[4 * e], q = s_edges[4 * e + 1], i = 4 * e;
            s_drawn[i] = (short)s_sx[p];
            s_drawn[i + 1] = (short)s_sy[p];
            s_drawn[i + 2] = (short)s_sx[q];
            s_drawn[i + 3] = (short)s_sy[q];
            Line(e, 1);
            s_lines++;
        }
        for (int e = 0; e < Edges; e++)
            if (s_on[e])
                Line(e, 1);
    }

    // One picture of the animation: frame 0 is a point, from Grow on the ship has its size. The distance goes down
    // as one over the frame, so that the ship's size on the screen grows evenly.
    static void Frame(int frame)
    {
        // Each axis at a speed of its own, in quarters of a 256th of a turn.
        Turn((frame * 5) >> 2, (frame * 7) >> 2, (frame * 3) >> 2);
        int size = frame < Grow ? frame : Grow;
        if (size == 0)
        {
            Bitmap.Plot(160, 120, 1);
            return;
        }
        Project(Distance * Grow / size);
        CX16.WaitFrame();
        if (frame == 1)
            Bitmap.Plot(160, 120, 0);
        Draw();
    }

    // ---- the checks ------------------------------------------------------------------------------

    static int s_failures;
    static int s_checks;
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= name;
        }
    }

    // The face's normal, outwards, from its corners in space, times the way to a point.
    static int Side(int[] x, int[] y, int[] z, int face, int px, int py, int pz)
    {
        int p = s_faces[3 * face], q = s_faces[3 * face + 1], r = s_faces[3 * face + 2];
        int ax = x[q] - x[p], ay = y[q] - y[p], az = z[q] - z[p], bx = x[r] - x[p], by = y[r] - y[p], bz = z[r] - z[p];
        return (ay * bz - az * by) * (px - x[p]) + (az * bx - ax * bz) * (py - y[p]) + (ax * by - ay * bx) * (pz - z[p]);
    }

    static bool ShapeIsClosedAndConvex()
    {
        int[] x = new int[Corners], y = new int[Corners], z = new int[Corners];
        for (int i = 0; i < Corners; i++)
        {
            x[i] = s_x[i];
            y[i] = s_y[i];
            z[i] = s_z[i];
        }
        bool ok = Corners - Edges + Faces == 2;
        for (int f = 0; f < Faces; f++)
            for (int i = 0; i < Corners; i++)
                ok &= Side(x, y, z, f, x[i], y[i], z[i]) <= 0;      // no corner in front of any face
        for (int e = 0; e < Edges; e++)                             // both corners of an edge lie in both its faces
            for (int k = 0; k < 4; k++)
            {
                int corner = s_edges[4 * e + k / 2], face = s_edges[4 * e + 2 + k % 2];
                ok &= Side(x, y, z, face, x[corner], y[corner], z[corner]) == 0;
            }
        return ok;
    }

    // The faces that the screen rule shows are those that face the eye, in space. Seen all but edge on, the
    // rounding to whole pixels decides, so those may go either way.
    static bool HiddenAsInSpace(out int shown)
    {
        bool ok = true;
        shown = 0;
        for (int turn = 0; turn < 24; turn++)
        {
            Turn(turn * 11, turn * 29 + 3, turn * 7 + 5);
            Project(Distance);
            for (int f = 0; f < Faces; f++)
            {
                int side = Side(s_rx, s_ry, s_rz, f, 0, 0, 0);
                if (side > 40000 || side < -40000)
                    ok &= s_shows[f] == (side > 0);
                if (s_shows[f])
                    shown++;
            }
        }
        return ok;
    }

    // Whether the middle of every drawn line has a lit pixel (the line helper may be a pixel off), or none has.
    static bool Lit(bool expected)
    {
        bool ok = true;
        for (int e = 0; e < Edges; e++)
        {
            if (!s_on[e])
                continue;
            int i = 4 * e;
            int x = (s_drawn[i] + s_drawn[i + 2]) / 2, y = (s_drawn[i + 1] + s_drawn[i + 3]) / 2;
            bool lit = false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    lit |= Bitmap.Get(x + dx, y + dy) == 1;
            ok &= lit == expected;
        }
        return ok;
    }

    static bool NothingLeft()
    {
        for (int y = 16; y < 224; y++)
            for (int x = 50; x < 270; x += 1)
                if (Bitmap.Get(x, y) != 0)
                    return false;
        return true;
    }

    static void Checks()
    {
        Check("the shape is closed and convex", ShapeIsClosedAndConvex());

        Turn(0, 0, 0);                          // nose away from the eye
        Project(Distance);
        Check("straight on: the nose in the middle", s_sx[0] == 160 && s_sy[0] == 120 - -2 * Focal / (Distance + 96));
        Check("straight on: a wing tip", s_sx[2] == 160 + 88 * Focal / (Distance - 36));
        Check("from behind: the stern shows, the top of the nose doesn't", s_shows[10] && !s_shows[1] && !s_shows[4]);
        Turn(0, 128, 0);                        // and towards it
        Project(Distance);
        Check("from the front: the stern is hidden", !s_shows[10] && s_sx[2] < 160);
        Turn(64, 0, 0);                         // nose up or down: a quarter turn keeps lengths
        Project(Distance);
        Check("a quarter turn", s_rz[0] - Distance == -2 && s_ry[0] == -96 && s_rx[0] == 0);

        int shown;
        Check("hidden faces as in space", HiddenAsInSpace(out shown));
        Check("some faces show, some don't", shown > 24 * 2 && shown < 24 * (Faces - 2));

        Turn(20, 30, 10);
        Project(Distance);
        Draw();
        Check("lines are drawn", s_lines >= 6 && s_lines < Edges && Lit(true));
        Turn(80, 200, 150);
        Project(Distance);
        Draw();
        Check("the next picture replaces it", s_lines >= 6 && Lit(true));
        Erase();
        Check("and drawn over again", s_lines == 0 && NothingLeft());
    }

    public static int Main()
    {
        Bitmap.Enable();
        Bitmap.Clear(0);
        CX16.SetColor(1, 15, 15, 15);
        Checks();

        Bitmap.Clear(0);
        Bitmap.Text(132, 4, "JAMESON", 1);
        Bitmap.Text(92, 230, "press any key", 1);
        int frame = 0;
        while (frame <= Grow + 8 || CX16.ReadKey() == 0)
        {
            Frame(frame);
            frame++;
        }
        Erase();
        Check("nothing left behind by the animation", NothingLeft());

        CX16.SetMode(CX16.Text80x60);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
