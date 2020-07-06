using System;
using System.Collections.Generic;

namespace GCodeMachine
{
	public class ProgramSequencer
	{
        public Sequence MainProgram { get; private set; }
        private readonly Dictionary<int, Sequence> subprograms;
        public IReadOnlyDictionary<int, Sequence> Subprograms => subprograms;

        private readonly Dictionary<int, int> subbegin;
        public IReadOnlyDictionary<int, int> SubprogramStart => subbegin;

        public ProgramSequencer()
		{
            subbegin = new Dictionary<int, int>();
            subprograms = new Dictionary<int, Sequence>();
            MainProgram = new Sequence();
		}

        public void SequenceProgram(String[] lines)
        {
            Sequence program = new Sequence();
            for (int id = 0; id < lines.Length; id++)
            {
                Arguments args = new Arguments(lines[id]);
                program.AddLine(args);
            }

            List<int> cursubs = new List<int>();
            Sequence current_sequence = MainProgram;
            for (int line = 0; line < program.Lines.Count; line++)
            {
                var arg = program.Lines[line];
                int prgid = -1;
                if (arg.SingleOptions.ContainsKey('N'))
                {
                    prgid = arg.SingleOptions['N'].ivalue1;
                }
                else if (arg.SingleOptions.ContainsKey('O'))
                {
                    prgid = arg.SingleOptions['O'].ivalue1;
                }

                if (prgid >= 0)
                {
                    current_sequence.AddLine(new Arguments(String.Format("M97 P{0}", prgid)));
                    current_sequence.AddLine(new Arguments("M99"));

                    cursubs.Add(prgid);
                    subbegin[prgid] = line;

                    current_sequence = new Sequence();
                    subprograms[prgid] = current_sequence;
                }

                current_sequence.AddLine(arg);

                foreach (var mcmd in arg.MCommands)
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
