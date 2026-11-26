// The Commander X16's SD card, or a drive on its serial bus, beyond reading and writing files (that
// is File, lib/File.cs): what is in a folder, and commands to the drive. Only for programs built with
// ./build.sh --cx16; the functions are in cx16/runtime.c, and a program for another target that calls
// them won't link.
//
//   if (Disk.OpenDirectory())                     // the current folder
//   {
//       char[] name = new char[80];
//       while (Disk.Next())
//       {
//           int length = Disk.CopyName(name);
//           Out.Line(new ReadOnlySpan<char>(name, 0, length));
//       }
//   }
//
// OpenDirectory reads the whole folder at once, into the topmost bank of the banked RAM (a program that
// lists folders has one bank less for Banks), because CMDR-DOS can't have a listing open while files are
// used. So the loop may open, write and delete files: it goes through the folder as it was. The bank
// holds some 250 entries with ordinary names (IsComplete says false if there were more). "." and ".."
// are left out.
// Names come as the drive has them, a byte a character. In the emulator's host folder (-fsroot) a
// folder's name is not looked at: it always lists the root. On an SD card it is. For the same reason
// give Command plain paths ("s:folder/name", not "s/folder/:name"): there the second removes the
// root's "name".

using System;
using System.Runtime.InteropServices;

public static class Disk
{
    [DllImport("libc")] static unsafe extern int cx16_dir_open(byte* path);
    [DllImport("libc")] static safe extern int cx16_dir_next();
    [DllImport("libc")] static safe extern void cx16_dir_close();
    [DllImport("libc")] static safe extern int cx16_dir_is_complete();
    [DllImport("libc")] static safe extern int cx16_dir_name_length();
    [DllImport("libc")] static safe extern int cx16_dir_name_char(int i);
    [DllImport("libc")] static safe extern int cx16_dir_size();
    [DllImport("libc")] static safe extern int cx16_dir_is_folder();
    [DllImport("libc")] static unsafe extern int cx16_dos(byte* command);

    const int MaxText = 80;

    // As the drive wants it: a byte a character, ended by 0. False for a text that is too long or isn't Latin-1.
    static bool ToBytes(ReadOnlySpan<char> text, Span<byte> bytes)
    {
        if (text.Length >= bytes.Length)
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == 0 || text[i] > 255)
                return false;
            bytes[i] = (byte)text[i];
        }
        bytes[text.Length] = 0;
        return true;
    }

    /// <summary>
    /// Reads a folder, for Next to hand out: "" for the current one, "outer/inner" from there or "/outer" from the root, with "#9:" in front for
    /// another device. False if there is no such device. A folder that isn't there has no entries.
    /// </summary>
    public static bool OpenDirectory(ReadOnlySpan<char> folder = default)
    {
        Span<byte> bytes = stackalloc byte[MaxText];
        if (!ToBytes(folder, bytes))
            return false;
        unsafe
        {
            // SAFETY: the bytes end in 0 and are only read during the call.
            fixed (byte* p = bytes)
                return cx16_dir_open(p) != 0;
        }
    }

    /// <summary>Goes to the next file or folder; false when there are no more.</summary>
    public static bool Next() => cx16_dir_next() != 0;

    /// <summary>Whether the folder read last fitted, so that Next hands out all of it.</summary>
    public static bool IsComplete => cx16_dir_is_complete() != 0;

    /// <summary>Forgets the folder: Next says false until another is read.</summary>
    public static void CloseDirectory() => cx16_dir_close();

    /// <summary>Copies the entry's name into a buffer (80 characters hold any) and returns its length; what doesn't fit is left off.</summary>
    public static int CopyName(Span<char> into)
    {
        int length = cx16_dir_name_length();
        if (length > into.Length)
            length = into.Length;
        for (int i = 0; i < length; i++)
            into[i] = (char)cx16_dir_name_char(i);
        return length;
    }

    /// <summary>A file's size in bytes (0 for a folder). From a drive on the serial bus, which counts in blocks, it is the blocks times 254: the size or a little more.</summary>
    public static int Size => cx16_dir_size();

    /// <summary>Whether the entry is a folder.</summary>
    public static bool IsFolder => cx16_dir_is_folder() != 0;

    /// <summary>
    /// Sends a command to the drive, such as "md:levels" (make a folder), "cd:levels", "cd:..", "rd:levels" or
    /// "r:new=old", with "#9:" in front for another device. Returns the drive's answer: 0 or 1 for done, a CBM
    /// DOS error number otherwise (62: not found, 63: it is there already), -1 if there is no such drive.
    /// </summary>
    public static int Command(ReadOnlySpan<char> command)
    {
        Span<byte> bytes = stackalloc byte[MaxText];
        if (!ToBytes(command, bytes))
            return -1;
        unsafe
        {
            // SAFETY: the bytes end in 0 and are only read during the call.
            fixed (byte* p = bytes)
                return cx16_dos(p);
        }
    }
}
