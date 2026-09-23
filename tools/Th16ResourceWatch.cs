// Read-only TH16 resource observer for development diagnostics.
// Copyright (C) 2026 StarLorentz. GPL-3.0-or-later.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

internal static class Th16ResourceWatch
{
    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const string SupportedHash = "C11776019F083978E66027E7394DAFB1FB9543AFCA986F28049A49417E341929";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static int ReadInt32(IntPtr handle, long address)
    {
        byte[] buffer = new byte[4];
        IntPtr count;
        if (!ReadProcessMemory(handle, new IntPtr(address), buffer, 4, out count) || count.ToInt64() != 4)
            throw new IOException("ReadProcessMemory failed at 0x" + address.ToString("X"));
        return BitConverter.ToInt32(buffer, 0);
    }

    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty);
    }

    private static int Main(string[] args)
    {
        int seconds = 120;
        if (args.Length > 0 && (!Int32.TryParse(args[0], out seconds) || seconds < 1 || seconds > 600))
        {
            Console.WriteLine("Usage: Th16ResourceWatch.exe [seconds: 1-600]");
            return 2;
        }

        Process[] games = Process.GetProcessesByName("th16");
        if (games.Length != 1)
        {
            Console.WriteLine("Expected exactly one th16.exe process; found " + games.Length + ".");
            return 2;
        }

        using (Process game = games[0])
        {
            string hash = HashFile(game.MainModule.FileName);
            if (!String.Equals(hash, SupportedHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Unsupported TH16 version: " + hash);
                return 2;
            }

            long baseAddress = game.MainModule.BaseAddress.ToInt64();
            IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation, false, game.Id);
            if (handle == IntPtr.Zero)
            {
                Console.WriteLine("Could not open TH16 for read-only observation.");
                return 2;
            }

            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "th16-resources-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            try
            {
                using (StreamWriter writer = new StreamWriter(output, false, new System.Text.UTF8Encoding(false)))
                {
                    writer.WriteLine("time,pid,lives,bombs,bombFragments,power,bombActive,bombType");
                    string previous = null;
                    Stopwatch timer = Stopwatch.StartNew();
                    while (timer.Elapsed.TotalSeconds < seconds && !game.HasExited)
                    {
                        try
                        {
                            int lives = ReadInt32(handle, baseAddress + 0xA57F4);
                            int bombs = ReadInt32(handle, baseAddress + 0xA5800);
                            int fragments = ReadInt32(handle, baseAddress + 0xA5804);
                            int power = ReadInt32(handle, baseAddress + 0xA57E4);
                            int bombPointer = ReadInt32(handle, baseAddress + 0xA6DA8);
                            int bombActive = -1;
                            int bombType = -1;
                            if (bombPointer != 0)
                            {
                                long bombAddress = (long)(uint)bombPointer;
                                bombActive = ReadInt32(handle, bombAddress + 0x30);
                                bombType = ReadInt32(handle, bombAddress + 0xD4);
                            }
                            string state = String.Format("{0},{1},{2},{3},{4},{5}",
                                lives, bombs, fragments, power, bombActive, bombType);
                            if (state != previous)
                            {
                                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "," + game.Id + "," + state;
                                writer.WriteLine(line);
                                writer.Flush();
                                Console.WriteLine(line);
                                previous = state;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Observation stopped: " + ex.Message);
                            break;
                        }
                        Thread.Sleep(100);
                    }
                }
            }
            finally { CloseHandle(handle); }
            Console.WriteLine("Saved: " + output);
            return 0;
        }
    }
}
