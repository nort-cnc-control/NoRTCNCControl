using System;
using System.Collections.Generic;
using GCodeMachine;

namespace ProgramBuilder
{
    public class Sequence
    {
        private List<Arguments> lines;
        public IReadOnlyList<Arguments> Lines => lines;

        public Sequence()
        {
            lines = new List<Arguments>();
        }

        public Arguments AddLine(Arguments line)
        {
            lines.Add(line);
            return line;
        }
    }
}
