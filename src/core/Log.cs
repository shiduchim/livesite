using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EKTriangle.Core
{
    /// <summary>Human-readable operation log. Writes to a bounded file and keeps lines in memory.</summary>
    public sealed class Log
    {
        const long MaxBytes = 1024 * 1024;
        readonly string _path;
        public readonly List<string> Lines = new List<string>();

        public Log(string path)
        {
            _path = path;
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = Path.Combine(fi.DirectoryName, Path.GetFileNameWithoutExtension(_path) + ".old.log");
                    FileUtil.TryDelete(old);
                    File.Move(_path, old);
                }
            }
            catch { }
        }

        public string FilePath => _path;

        public void Info(string message) => Write("INFO ", message);
        public void Warn(string message) => Write("WARN ", message);
        public void Error(string message) => Write("ERROR", message);

        void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + message;
            Lines.Add(line);
            if (_path == null) return;
            try { File.AppendAllText(_path, line + "\r\n", Encoding.UTF8); } catch { }
        }
    }
}
