using System;

namespace Log
{
    public class Logger
    {
        private static Logger instance;

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
            Console.WriteLine("{0,16} : {1,16} : {2}", source.Name, type, message);
        }

        public void Debug(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, 1);
        }

        public void Error(ILoggerSource source, string type, string message)
        {
            Log(source, type, message, -1);
        }
    }
}
