using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameBoyEmu.Debugger
{
    public static class Logger
    {
        public static StreamWriter LogWriter;
        public static string filePath;
        public static bool LogEnable = false;
        private static DateTime timeStarted;
        private static DateTime timeEnded;
        private static TimeSpan difference;
        public static void InitLogWriter()
        {
            filePath = Path.Combine(AppContext.BaseDirectory, "emulator_log.txt");
            LogWriter = new StreamWriter(filePath, false);
            LogWriter.Flush();
            Write(" LOG START ");
            timeStarted = DateTime.Now;
            Write($"TIME : {timeStarted.ToString()}");
        }

        public static void Write(string input)
        {
            if (!LogEnable | string.IsNullOrEmpty(input)) 
            {
                return;
            }
            else
            {
                LogWriter.WriteLine(input);
            }
        }

        public static void TerminateLogging()
        {
            if (LogWriter == null)
            {
                return;
            }
            Write(" LOG END ");
            timeEnded = DateTime.Now;
            Write($"TIME : {timeEnded.ToString()}");
            difference = timeEnded - timeStarted;
            Write($"ELAPSED TIME : {difference.ToString()}");
            LogWriter.Flush();
            LogWriter.Close();
            LogWriter = null;
        }
    }
}
