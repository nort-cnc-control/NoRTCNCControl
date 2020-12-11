using System.Linq;
using System;
using System.Collections.Generic;

namespace GCodeMachine
{
    public class Arguments
    {
        public class Option
        {
            #region code
            public enum CodeType
            {
                Code,
                Variable,
            }

            public CodeType codeType;
            public char letter;
            public int varid;
            #endregion

            #region value
            public enum ValueType
            {
                Number,
                Expression
            }

            public ValueType type;

            public String value1;
            public int ivalue1;
            public String value2;
            public int ivalue2;

            public decimal value;
            public bool dot;

            public Expression expr;
            #endregion
        }

        private List<Option> options;
        public IReadOnlyList<Option> Options => options;

        private Dictionary<Char, Option> singleOptions;
        public IReadOnlyDictionary<Char, Option> SingleOptions => singleOptions;

        private (Option, String) GetOption(String s)
        {
            int i = 0;
            int len = s.Length;
            while (i < len && s[i] == ' ')
                ++i;

            Option opt = new Option();

            #region parse code
            if (s[i] == '#')
            {
                i++;
                opt.codeType = Option.CodeType.Variable;
                int j;
                for (j = i; j < s.Length && s[j] != '='; j++)
                {}

                if (j == s.Length)
                    return (null, s);

                var num = s.Substring(i, j - i);
                opt.varid = int.Parse(num);
                i = j + 1;
            }
            else
            {
                if (i >= len || !Char.IsLetter(s[i]))
                    return (null, s);

                opt.codeType = Option.CodeType.Code;
                opt.letter = s[i];
                ++i;
            }

            #endregion

            #region parse value
            if (s[i] == '[')
            {
                ++i;
                opt.type = Option.ValueType.Expression;
                var expr = "";
                while (i < len)
                {
                    if (s[i] == ']')
                    {
                        ++i;
                        opt.expr = new Expression(expr);
                        return (opt, s.Substring(i));
                    }
                    expr += s[i++];
                }
            }
            else
            {
                opt.type = Option.ValueType.Number;
                if (i >= len || !(Char.IsDigit(s[i]) || s[i] == '-'))
                    return (null, s);
                String val = "";
                while (i < len && (Char.IsDigit(s[i]) || s[i] == '-'))
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
                        return (null, s);
                    val = "";
                    while (i < len && Char.IsDigit(s[i]))
                    {
                        val += s[i];
                        ++i;
                    }
                    opt.value2 = val;
                    opt.ivalue2 = Int32.Parse(val);
                    opt.value = Decimal.Parse(opt.value1 + "." + opt.value2, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    opt.dot = false;
                    opt.value2 = "";
                    opt.ivalue2 = 0;
                    opt.value = opt.ivalue1;
                }
            }
            #endregion
            return (opt, s.Substring(i));
        }

        private string CutComments(string line)
        {
            string result = "";
            int brackets = 0;
            int sqbrackets = 0;
            bool escape = false;
            foreach (char c in line)
            {
                if (!escape)
                {
                    if (c == ';')
                    {
                        break;
                    }
                    else if (c == '%')
                    {
                    }
                    else if (c == '(' && sqbrackets == 0)
                    {
                        brackets++;
                    }
                    else if (c == ')' && sqbrackets == 0)
                    {
                        if (brackets > 0)
                            brackets--;
                        else
                            throw new ArgumentOutOfRangeException();
                    }
                    else if (c == '[' && brackets == 0)
                    {
                        if (sqbrackets > 0)
                            throw new ArgumentOutOfRangeException();

                        sqbrackets++;
                        result += '[';
                    }
                    else if (c == ']' && brackets == 0)
                    {
                        if (sqbrackets > 0)
                            sqbrackets--;
                        else
                            throw new ArgumentOutOfRangeException();
                        result += ']';
                    }
                    else
                    {
                        if (brackets == 0)
                        {
                            result += c;
                        }
                    }
                }
                else
                {
                    result += c;
                    escape = false;
                }

                if (brackets > 0)
                    escape = (c == '\\');
                else
                    escape = false;
            }
            return result;
        }

        public Arguments(String line)
        {
            line = line.ToUpper();
            options = new List<Option>();
            singleOptions = new Dictionary<char, Option>();
            line = CutComments(line);
            while (line.Length > 0)
            {
                var (opt, sl) = GetOption(line);
                line = sl;
                if (opt == null)
                    break;
                if (opt.letter != 'G' && opt.letter != 'M')
                    singleOptions[opt.letter] = opt;
                options.Add(opt);
            }
        }

        public Arguments()
        {
            options = new List<Option>();
            singleOptions = new Dictionary<char, Option>();
        }

        public void AddOption(Option opt)
        {
            options.Add(opt);
            if (opt.letter != 'G' && opt.letter != 'M')
                singleOptions[opt.letter] = opt;
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

        public Option Q
        {
            get
            {
                if (!singleOptions.ContainsKey('Q'))
                    return null;
                return singleOptions['Q'];
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
