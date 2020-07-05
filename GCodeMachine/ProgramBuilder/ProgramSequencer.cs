using System;
using System.Collections.Generic;

namespace GCodeMachine
{
	public class ProgramSequencer
	{
        public Sequence MainProgram { get; private set; }
        private readonly Dictionary<int, Sequence> subprograms;
        public IReadOnlyDictionary<int, Sequence> Subprograms => subprograms;

        public ProgramSequencer()
		{
            subprograms = new Dictionary<int, Sequence>();
            MainProgram = new Sequence();
		}

        public void SequenceProgram(String[] lines)
        {
            Sequence program = new Sequence();
            foreach (var line in lines)
            {
                Arguments args = new Arguments(line);
                program.AddLine(args);
            }

            List<int> cursubs = new List<int>();
            Sequence current_sequence = MainProgram;
            foreach (var args in program.Lines)
            {
                int prgid = -1;
                if (args.SingleOptions.ContainsKey('N'))
                {
                    prgid = args.SingleOptions['N'].ivalue1;
                }
                else if (args.SingleOptions.ContainsKey('O'))
                {
                    prgid = args.SingleOptions['O'].ivalue1;
                }

                if (prgid >= 0)
                {
                    current_sequence.AddLine(new Arguments(String.Format("M97 P{0}", prgid)));
                    current_sequence.AddLine(new Arguments("M99"));

                    cursubs.Add(prgid);

                    current_sequence = new Sequence();
                    subprograms[prgid] = current_sequence;
                }

                current_sequence.AddLine(args);

                foreach (var mcmd in args.MCommands)
                {
                    if (mcmd.ivalue1 == 99)
                    {
                        cursubs.Clear();
                        current_sequence = MainProgram;
                    }
                }
            }
        }
	}
}
