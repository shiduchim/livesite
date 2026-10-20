using System;
using System.Collections.Generic;
using System.IO;

namespace EKRibbon
{
    // last-error.log collects problems from every Revit year; startup_YEAR.txt is a
    // short summary of the last ribbon build. Logging never throws.
    internal static class Log
    {
        static readonly HashSet<string> reported=new HashSet<string>();
        static string ErrorFile {get{return Path.Combine(State.Root,"last-error.log");}}

        internal static void Error(string where,Exception ex)
        {
            try
            {
                Directory.CreateDirectory(State.Root);
                var info=new FileInfo(ErrorFile);
                if(info.Exists&&info.Length>1024*1024)
                {
                    string old=ErrorFile+".old";
                    if(File.Exists(old))File.Delete(old);
                    File.Move(ErrorFile,old);
                }
                File.AppendAllText(ErrorFile,DateTime.Now.ToString("s")+" [Revit "+Application.Year+"] "+where+": "+ex+Environment.NewLine);
            }
            catch{}
        }

        internal static void ErrorOnce(string where,Exception ex)
        {
            lock(reported)if(!reported.Add(where))return;
            Error(where,ex);
        }

        internal static void Startup(int year,IEnumerable<string> lines)
        {
            try
            {
                Directory.CreateDirectory(State.Root);
                File.WriteAllLines(Path.Combine(State.Root,"startup_"+year+".txt"),lines);
            }
            catch{}
        }
    }
}
