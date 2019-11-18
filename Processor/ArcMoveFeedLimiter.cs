using System;
using ActionProgram;
using Actions;
using Config;

namespace Processor
{
    public class ArcMoveFeedLimiter : IProcessor
    {
        public void ProcessProgram(ActionProgram.ActionProgram program)
        {
            foreach (var action in program.Actions)
            {
                var ma = action.action as RTAction;
                if (ma == null)
                    continue;
                var arcmovecmd = ma.Command as RTArcMoveCommand;
                if (arcmovecmd == null)
                    continue;
                double R = arcmovecmd.R;
                double maxfeed = Math.Sqrt(R*config.max_acceleration);
                arcmovecmd.Options.Feed = Math.Min(arcmovecmd.Options.Feed, maxfeed);
            }
        }

        private MachineParameters config;
        public ArcMoveFeedLimiter(MachineParameters config)
        {
            this.config = config;
        }
    }
}
