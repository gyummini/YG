using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Tagged logging with a small in-memory ring buffer (debug overlay) and an optional per-instance file.</summary>
    public static class GameLog
    {
        public struct Entry
        {
            public float Time;
            public string Tag;
            public string Message;
            public LogType Type;
        }

        const int Capacity = 200;
        static readonly Queue<Entry> s_Entries = new Queue<Entry>(Capacity);
        static StreamWriter s_File;

        public static IEnumerable<Entry> Entries => s_Entries;

        public static void Info(string tag, string message) => Write(tag, message, LogType.Log);
        public static void Warn(string tag, string message) => Write(tag, message, LogType.Warning);
        public static void Error(string tag, string message) => Write(tag, message, LogType.Error);

        static void Write(string tag, string message, LogType type)
        {
            var line = $"[NO:{tag}] {message}";
            switch (type)
            {
                case LogType.Warning: Debug.LogWarning(line); break;
                case LogType.Error: Debug.LogError(line); break;
                default: Debug.Log(line); break;
            }

            if (s_Entries.Count >= Capacity) s_Entries.Dequeue();
            s_Entries.Enqueue(new Entry { Time = Time.realtimeSinceStartup, Tag = tag, Message = message, Type = type });

            if (s_File != null)
            {
                try
                {
                    s_File.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                    s_File.Flush();
                }
                catch (Exception)
                {
                    s_File = null;
                }
            }
        }

        /// <summary>Mirror all game logs into a file (used by automated MPPM tests).</summary>
        public static void OpenFile(string path)
        {
            CloseFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                s_File = new StreamWriter(fs);
                Application.quitting -= CloseFile;
                Application.quitting += CloseFile;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NO:Log] could not open {path}: {e.Message}");
            }
        }

        public static void CloseFile()
        {
            try
            {
                s_File?.Dispose();
            }
            catch (Exception)
            {
                // ignored
            }
            s_File = null;
        }
    }
}
