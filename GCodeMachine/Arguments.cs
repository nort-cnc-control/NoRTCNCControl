using System.Linq;
using System;
using System.Collections.Generic;

namespace GCodeMachine
{
    public class Arguments
    {
        public class Option
        {
            public char letter;
            public String value1;
            public int ivalue1;
            public String value2;
            public int ivalue2;
            public double value;
            public bool dot;
        }

        private List<Option> options;
        public IReadOnlyList<Option> Options => options;

        private Dictionary<Char, Option> singleOptions;

        private Tuple<Option, String> GetOption(String s)
        {
            int i = 0;
            int len = s.Length;
            while (i < len && s[i] == ' ')
                ++i;
            if (i >= len || !Char.IsLetter(s[i]))
                return null;
            Option opt = new Option();
            opt.letter = s[i];
            ++i;
            if (i >= len || !Char.IsDigit(s[i]))
                return null;
            String val = "";
            while (i < len && Char.IsDigit(s[i]))
            {
                val += s[i];
                ++i;
            }
            opt.value1 = val;
            opt.ivalue1 = Int32.Parse(val);
            if (i < len && s[i] == '.')
            {
                opt.dot = true;
                ++i;
                if (!Char.IsDigit(s[i]))
                    return null;
                val = "";
                while (i < len && Char.IsDigit(s[i]))
                {
                    val += s[i];
                    ++i;
                }
                opt.value2 = val;
                opt.ivalue2 = Int32.Parse(val);
                opt.value = Double.Parse(opt.value1 + "." + opt.value2);
            }
            else
            {
                opt.dot = false;
                opt.value2 = "";
                opt.ivalue2 = 0;
                opt.value = opt.ivalue1;
            }
            return new Tuple<Option, String>(opt, s.Substring(i));
        }

        public Arguments(String line)
        {
            options = new List<Option>();
            singleOptions = new Dictionary<char, Option>();
            while (line.Length > 0)
            {
                var res = GetOption(line);
                if (res == null)
                    break;
                Option opt = res.Item1;
                line = res.Item2;
                if (opt.letter != 'G' && opt.letter != 'M')
                    singleOptions[opt.letter] = opt;
                options.Add(opt);
            }
        }

        public Option LineNumber
        {
            get
            {
                if (!singleOptions.ContainsKey('N'))
                    return null;
                return singleOptions['N'];
            }
        }

        public IEnumerable<Option> GCommands
        {
            get
            {
                return options.Where(opt => opt.letter == 'G');
            }
        }

        public IEnumerable<Option> MCommands
        {
            get
            {
                return options.Where(opt => opt.letter == 'M');
            }
        }

        public Option X
        {
            get
            {
                if (!singleOptions.ContainsKey('X'))
                    return null;
                return singleOptions['X'];
            }
        }

        public Option Y
        {
            get
            {
                if (!singleOptions.ContainsKey('Y'))
                    return null;
                return singleOptions['Y'];
            }
        }

        public Option Z
        {
            get
            {
                if (!singleOptions.ContainsKey('Z'))
                    return null;
                return singleOptions['Z'];
            }
        }

        public Option I
        {
            get
            {
                if (!singleOptions.ContainsKey('I'))
                    return null;
                return singleOptions['I'];
            }
        }

        public Option J
        {
            get
            {
                if (!singleOptions.ContainsKey('J'))
                    return null;
                return singleOptions['J'];
            }
        }

        public Option K
        {
            get
            {
                if (!singleOptions.ContainsKey('K'))
                    return null;
                return singleOptions['K'];
            }
        }

        public Option R
        {
            get
            {
                if (!singleOptions.ContainsKey('R'))
                    return null;
                return singleOptions['R'];
            }
        }

        public Option Feed
        {
            get
            {
                if (!singleOptions.ContainsKey('F'))
                    return null;
                return singleOptions['F'];
            }
        }

        public Option Speed
        {
            get
            {
                if (!singleOptions.ContainsKey('S'))
                    return null;
                return singleOptions['S'];
            }
        }
    }
}
