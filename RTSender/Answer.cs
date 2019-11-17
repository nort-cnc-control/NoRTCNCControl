using System;
using System.Collections.Generic;
using System.Linq;

namespace RTSender
{
    public class Answer
    {
        public string Message { get; private set; }
        public Dictionary<String, String> Values { get; private set; }

        public Answer(string line)
        {
            Values = new Dictionary<string, string>();
            string[] args = line.Split(' ');
            Message = args[0];
            for (int i = 1; i < args.Length; ++i)
            {
                string val = args[i];
                string[] kv = val.Split(':');
                Values[kv[0]] = kv[1];
            }
        }
    }
}
