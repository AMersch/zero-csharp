// SDL 3 for zero-mode C#: windows, 2D drawing, keyboard and mouse, the clock.
// A thin layer over SDL's C functions, named as in SDL without the SDL_ prefix.
//
// Every signature is blittable: zero mode binds [DllImport]s when linking and
// marshals nothing. SDL's C bool is one byte, so the imports return byte and
// the public methods turn it into bool. The ZeroLinkLibrary item in the
// project file links libSDL3 and binds the "SDL3" imports.

using System;
using System.Runtime.InteropServices;

namespace Sdl3;

/// <summary>SDL_Init's subsystems.</summary>
[Flags]
public enum InitFlags : uint
{
    Audio = 0x10,
    Video = 0x20,
    Joystick = 0x200,
    Gamepad = 0x2000,
    Events = 0x4000,
}

/// <summary>SDL_CreateWindow's flags.</summary>
[Flags]
public enum WindowFlags : ulong
{
    None = 0,
    Fullscreen = 0x1,
    Resizable = 0x20,
    HighPixelDensity = 0x2000,
}

/// <summary>The event types this binding reads; others come through as their number.</summary>
public enum EventType : uint
{
    Quit = 0x100,
    KeyDown = 0x300,
    KeyUp = 0x301,
    MouseMotion = 0x400,
    MouseButtonDown = 0x401,
    MouseButtonUp = 0x402,
}

/// <summary>Physical keys (SDL_Scancode), by position on a US keyboard.</summary>
public enum Scancode : uint
{
    A = 4, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D1 = 30, D2, D3, D4, D5, D6, D7, D8, D9, D0,
    Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44,
    Right = 79, Left = 80, Down = 81, Up = 82,
}

/// <summary>A window (SDL_Window*).</summary>
public readonly struct Window
{
    readonly nint _handle;
    public bool IsNull => _handle == 0;
}

/// <summary>A window's 2D renderer (SDL_Renderer*).</summary>
public readonly struct Renderer
{
    readonly nint _handle;
    public bool IsNull => _handle == 0;
}

/// <summary>A rectangle in floating-point pixels (SDL_FRect).</summary>
public record struct FRect(float X, float Y, float W, float H);

/// <summary>
/// An event (SDL_Event, 128 bytes). Type says which; the other properties read
/// that kind's fields: Scancode, Down and Repeat a key's, X and Y the mouse's,
/// Button a mouse button's.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct Event
{
    [FieldOffset(0)] safe uint _type;
    [FieldOffset(24)] safe uint _scancode;
    [FieldOffset(24)] safe byte _button;
    [FieldOffset(28)] safe float _x;
    [FieldOffset(32)] safe float _y;
    [FieldOffset(36)] safe byte _keyDown;
    [FieldOffset(37)] safe byte _repeat;

    public readonly EventType Type => (EventType)_type;
    public readonly Scancode Scancode => (Scancode)_scancode;
    public readonly bool Down => _keyDown != 0;
    public readonly bool Repeat => _repeat != 0;
    public readonly float X => _x;
    public readonly float Y => _y;
    public readonly int Button => _button;
}

public static class SDL
{
    const string Lib = "SDL3";

    [DllImport(Lib)] static safe extern byte SDL_Init(InitFlags flags);
    [DllImport(Lib)] static safe extern void SDL_Quit();
    [DllImport(Lib)] static unsafe extern byte* SDL_GetError();
    [DllImport(Lib)] static unsafe extern Window SDL_CreateWindow(byte* title, int w, int h, WindowFlags flags);
    [DllImport(Lib)] static safe extern void SDL_DestroyWindow(Window window);
    [DllImport(Lib)] static unsafe extern byte SDL_SetWindowTitle(Window window, byte* title);
    [DllImport(Lib)] static unsafe extern Renderer SDL_CreateRenderer(Window window, byte* name);
    [DllImport(Lib)] static safe extern void SDL_DestroyRenderer(Renderer renderer);
    [DllImport(Lib)] static safe extern byte SDL_SetRenderVSync(Renderer renderer, int vsync);
    [DllImport(Lib)] static safe extern byte SDL_SetRenderDrawColor(Renderer renderer, byte r, byte g, byte b, byte a);
    [DllImport(Lib)] static safe extern byte SDL_RenderClear(Renderer renderer);
    [DllImport(Lib)] static unsafe extern byte SDL_RenderFillRect(Renderer renderer, FRect* rect);
    [DllImport(Lib)] static unsafe extern byte SDL_RenderRect(Renderer renderer, FRect* rect);
    [DllImport(Lib)] static safe extern byte SDL_RenderLine(Renderer renderer, float x1, float y1, float x2, float y2);
    [DllImport(Lib)] static safe extern byte SDL_RenderPresent(Renderer renderer);
    [DllImport(Lib)] static unsafe extern byte SDL_PollEvent(Event* e);
    [DllImport(Lib)] static unsafe extern byte* SDL_GetKeyboardState(int* numkeys);
    [DllImport(Lib)] static safe extern ulong SDL_GetTicks();
    [DllImport(Lib)] static safe extern void SDL_Delay(uint ms);

    public static bool Init(InitFlags flags) => SDL_Init(flags) != 0;
    public static void Quit() => SDL_Quit();

    /// <summary>The last error's message, as UTF-8 (Out.WriteUtf8 prints it).</summary>
    public static ReadOnlySpan<byte> GetError()
    {
        unsafe
        {
            // SAFETY: SDL returns a NUL-terminated string it keeps until the next error.
            byte* text = SDL_GetError();
            int length = 0;
            while (text[length] != 0) length++;
            return new ReadOnlySpan<byte>(text, length);
        }
    }

    /// <summary>A window; IsNull when it couldn't be made (see GetError).</summary>
    public static Window CreateWindow(ReadOnlySpan<byte> title, int width, int height, WindowFlags flags = WindowFlags.None)
    {
        Span<byte> text = stackalloc byte[256];
        Terminate(title, text);
        unsafe
        {
            // SAFETY: text is NUL-terminated and outlives the call; SDL copies it.
            fixed (byte* p = text) return SDL_CreateWindow(p, width, height, flags);
        }
    }

    public static void DestroyWindow(Window window) => SDL_DestroyWindow(window);

    public static bool SetWindowTitle(Window window, ReadOnlySpan<byte> title)
    {
        Span<byte> text = stackalloc byte[256];
        Terminate(title, text);
        unsafe
        {
            // SAFETY: as in CreateWindow.
            fixed (byte* p = text) return SDL_SetWindowTitle(window, p) != 0;
        }
    }

    /// <summary>The window's renderer, SDL's choice of driver; IsNull when it couldn't be made.</summary>
    public static Renderer CreateRenderer(Window window)
    {
        unsafe
        {
            // SAFETY: a null name asks SDL to choose.
            return SDL_CreateRenderer(window, null);
        }
    }

    public static void DestroyRenderer(Renderer renderer) => SDL_DestroyRenderer(renderer);
    /// <summary>1: present waits for the display (one frame per refresh); 0: it doesn't.</summary>
    public static bool SetRenderVSync(Renderer renderer, int vsync) => SDL_SetRenderVSync(renderer, vsync) != 0;
    public static bool SetRenderDrawColor(Renderer renderer, byte r, byte g, byte b, byte a = 255) => SDL_SetRenderDrawColor(renderer, r, g, b, a) != 0;
    public static bool RenderClear(Renderer renderer) => SDL_RenderClear(renderer) != 0;
    public static bool RenderLine(Renderer renderer, float x1, float y1, float x2, float y2) => SDL_RenderLine(renderer, x1, y1, x2, y2) != 0;
    public static bool RenderPresent(Renderer renderer) => SDL_RenderPresent(renderer) != 0;

    public static bool RenderFillRect(Renderer renderer, FRect rect)
    {
        unsafe
        {
            // SAFETY: rect is a local; SDL reads it during the call.
            return SDL_RenderFillRect(renderer, &rect) != 0;
        }
    }

    public static bool RenderRect(Renderer renderer, FRect rect)
    {
        unsafe
        {
            // SAFETY: as in RenderFillRect.
            return SDL_RenderRect(renderer, &rect) != 0;
        }
    }

    /// <summary>The next waiting event, if there is one.</summary>
    public static bool PollEvent(out Event e)
    {
        e = default;
        unsafe
        {
            // SAFETY: e is a 128-byte SDL_Event that SDL fills during the call.
            fixed (Event* p = &e) return SDL_PollEvent(p) != 0;
        }
    }

    /// <summary>Whether a key is held now (as of the last PollEvent).</summary>
    public static bool IsKeyDown(Scancode key)
    {
        unsafe
        {
            // SAFETY: SDL's array lives as long as SDL does, numkeys long.
            int count;
            byte* state = SDL_GetKeyboardState(&count);
            return (uint)key < (uint)count && state[(int)key] != 0;
        }
    }

    /// <summary>Milliseconds since Init.</summary>
    public static ulong GetTicks() => SDL_GetTicks();
    public static void Delay(uint milliseconds) => SDL_Delay(milliseconds);

    // UTF-8 text, cut to fit, then a NUL.
    static void Terminate(ReadOnlySpan<byte> text, Span<byte> into)
    {
        int length = Math.Min(text.Length, into.Length - 1);
        text.Slice(0, length).CopyTo(into);
        into[length] = 0;
    }
}
