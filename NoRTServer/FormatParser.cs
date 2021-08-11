using System;
using System.Linq;
using System.Collections.Generic;

namespace NoRTServer
{
	class FormatParser
	{
		IReadOnlyDictionary<char, bool> argtype;
		string progname;

		public FormatParser(string progname, string format)
		{
			this.progname = progname;
			argtype = ParseFormat(format);
		}

		private IReadOnlyDictionary<char, bool> ParseFormat(string format)
		{
			Dictionary<char, bool> argtype = new Dictionary<char, bool>();
			char last = '\x0';
			foreach (char c in format)
			{
				if (c == ':')
				{
					if (last != '\x0')
						argtype[last] = true;
					last = '\x0';
				}
				else
				{
					argtype[last] = false;
					last = c;
				}
			}
			if (last != '\x0')
				argtype[last] = false;
			return argtype;
		}

		public IReadOnlyList<Tuple<char, string>> ParseArgs(string[] args)
		{
			List<Tuple<char, string>> parsed = new List<Tuple<char, string>>();
			bool expectValue = false;
			char key = '\x0';
			if (args.Count() == 0)
				return parsed;
			if (args[0].EndsWith(progname))
				args = args.Skip(1).ToArray();
			foreach (var arg in args)
			{
				if (expectValue)
				{
					parsed.Add(new Tuple<char, string>(key, arg));
					expectValue = false;
				}
				else
				{
					if (arg.Length < 2)
						return null;
					key = arg[1];
					if (!argtype.ContainsKey(key))
					{
						Console.WriteLine(String.Format("Can not find key {0}", key));
						return null;
					}
						
					if (argtype[key])
					{
						expectValue = true;
					}
					else
					{
						parsed.Add(new Tuple<char, string>(key, null));
					}
				}
			}
			if (expectValue)
			{
				Console.WriteLine(String.Format("Expect value for key {0}", key));
				return null;
			}

			return parsed;
		}
	}
}
