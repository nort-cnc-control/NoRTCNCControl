using System;
using System.Collections.Generic;

namespace GCodeMachine
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
