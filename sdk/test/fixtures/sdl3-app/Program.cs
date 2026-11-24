// A square the arrow keys move, on a window SDL 3 opens; Escape or closing
// the window quits. "--frames N" stops after N frames (the tests run it so,
// with SDL_VIDEO_DRIVER=offscreen) and prints where the square ended up.

using System;
using Sdl3;

public static class Program
{
    public static int Main(string[] args)
    {
        int frames = -1;
        if (args.Length == 2 && args[0] == "--frames" && !Str.TryParse(args[1].AsSpan(), out frames))
            frames = 1;

        if (!SDL.Init(InitFlags.Video))
        {
            Out.Write("SDL_Init failed: ");
            Out.WriteUtf8(SDL.GetError());
            Out.Line("");
            Out.Flush();
            return 1;
        }
        Window window = SDL.CreateWindow("Zero C# and SDL 3"u8, 640, 480);
        Renderer renderer = window.IsNull ? default : SDL.CreateRenderer(window);
        if (renderer.IsNull)
        {
            Out.Write("no window: ");
            Out.WriteUtf8(SDL.GetError());
            Out.Line("");
            Out.Flush();
            SDL.Quit();
            return 1;
        }
        SDL.SetRenderVSync(renderer, 1);

        float x = 300, y = 220;
        bool running = true;
        int frame = 0;
        while (running && frame != frames)
        {
            while (SDL.PollEvent(out Event e))
            {
                if (e.Type == EventType.Quit) running = false;
                if (e.Type == EventType.KeyDown && e.Scancode == Scancode.Escape) running = false;
            }
            if (SDL.IsKeyDown(Scancode.Left)) x -= 4;
            if (SDL.IsKeyDown(Scancode.Right)) x += 4;
            if (SDL.IsKeyDown(Scancode.Up)) y -= 4;
            if (SDL.IsKeyDown(Scancode.Down)) y += 4;
            // Headless runs press nothing: drift, so the test sees the loop work.
            if (frames > 0) x += 1;

            SDL.SetRenderDrawColor(renderer, 24, 24, 40);
            SDL.RenderClear(renderer);
            SDL.SetRenderDrawColor(renderer, 240, 180, 40);
            SDL.RenderFillRect(renderer, new FRect(x, y, 40, 40));
            SDL.RenderPresent(renderer);
            if (frames > 0) SDL.Delay(1);
            frame++;
        }

        SDL.DestroyRenderer(renderer);
        SDL.DestroyWindow(window);
        SDL.Quit();
        Out.Line($"{frame} frames, square at {(int)x},{(int)y}");
        Out.Flush();
        return 0;
    }
}
