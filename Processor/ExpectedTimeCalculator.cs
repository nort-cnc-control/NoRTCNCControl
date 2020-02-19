using System;
using ActionProgram;
using Actions;

namespace Processor
{
    public class ExpectedTimeCalculator : IProcessor
    {
        public double ExecutionTime { get; private set; }
        public ActionProgram.ActionProgram ExecutionProgram { get; private set; }

        public ExpectedTimeCalculator()
        {
            ExecutionTime = 0;
            ExecutionProgram = null;
        }

        public void ProcessProgram(ActionProgram.ActionProgram program)
        {
            ExecutionProgram = program;
            if (program == null)
            {
                ExecutionTime = 0;
                return;
            }
            double time = 0;
            foreach (var action in program.Actions)
            {
                if (!(action.action is RTAction ma))
                    continue;
                if (!(ma.Command is IRTMoveCommand move))
                    continue;
                var len = move.Length;
                var feed = move.Options.Feed;
                var t = len / feed;
                time += t;
            }
            ExecutionTime = time;
        }
    }
}
