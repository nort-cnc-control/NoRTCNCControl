using System;

namespace Log
{
    public class Logger
    {
        private static Logger instance;

        private System.IO.StreamWriter writer = new System.IO.StreamWriter(Console.OpenStandardOutput());
        public System.IO.StreamWriter Writer
        {
            get => writer;
            set
            {
                writer = value;
                writer.AutoFlush = true;
            }
        }

        public static Logger Instance
        {
            get
            {
                if (instance == null)
                    instance = new Logger();
                return instance;
            }
        }

        public void Log(ILoggerSource source, string type, string message, int level)
        {
            String timeStamp = DateTime.Now.ToLongTimeString();
            lock (writer)
            {
                writer.WriteLine("[{0}] {1} | {2,16} : {3,16} : {4}", timeStamp, level, source.Name, type, message);
		writer.Flush();
            }
    	}

        public void Debug(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, 3);
        }

        public void Info(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, 2);
        }

        public void Warning(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, 1);
        }

        public void Error(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, 0);
        }
    }
}
