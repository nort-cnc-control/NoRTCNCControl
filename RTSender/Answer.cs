using System;
using System.Collections.Generic;
using System.Linq;

namespace RTSender
{
    public class Answer
    {
        public string Message { get; private set; }
        public Dictionary<Char, String> Values { get; private set; }

        public Answer(string line)
        {
            Values = new Dictionary<char, string>();
            char[] sep = { ' ' };
            string[] args = line.Split(sep);
            Message = args[0];
            for (int i = 1; i < args.Length; ++i)
            {
                string val = args[i];
                char C = val[0];
                string Z = val.Substring(2);
                Values[C] = Z;
            }
        }
    }
}
