using System;
using ActionProgram;
using Actions;
using Config;
using Vector;

namespace Processor
{
    public class MoveFeedLimiter : IProcessor
    {
        private (decimal feed, decimal acc) MaxLineFeedAcc(Vector3 dir)
        {
            decimal maxacc = config.max_acceleration;
            decimal feed, acc;
            acc = Decimal.MaxValue;
            if (Math.Abs(dir.x) > 1e-8m)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.x));
            if (Math.Abs(dir.y) > 1e-8m)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.y));
            if (Math.Abs(dir.z) > 1e-8m)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.z));

            decimal maxf_x = config.X_axis.maxfeed;
            decimal maxf_y = config.Y_axis.maxfeed;
            decimal maxf_z = config.Z_axis.maxfeed;

            feed = Decimal.MaxValue;
            if (Math.Abs(dir.x) > 1e-8m)
                feed = Math.Min(feed, maxf_x / Math.Abs(dir.x));
            if (Math.Abs(dir.y) > 1e-8m)
                feed = Math.Min(feed, maxf_y / Math.Abs(dir.y));
            if (Math.Abs(dir.z) > 1e-8m)
                feed = Math.Min(feed, maxf_z / Math.Abs(dir.z));
            return (feed, acc);
        }

        public void ProcessProgram(ActionProgram.ActionProgram program)
        {
            foreach (var action in program.Actions)
            {
                if (!(action.action is RTAction ma))
                    continue;
                if (ma.Command is RTLineMoveCommand linemovecmd)
                {
                    Vector3 dir = linemovecmd.DirStart; // Equal to DirEnd
                    var (maxfeed, maxacc) = MaxLineFeedAcc(dir);
                    linemovecmd.Options.acceleration = maxacc;
                    linemovecmd.Options.Feed = Math.Min(maxfeed, linemovecmd.Options.Feed);
                }

                if (ma.Command is RTArcMoveCommand arcmovecmd)
                {
                    decimal R = arcmovecmd.R;
                    decimal maxfeed = (decimal)Math.Sqrt((double)(R * config.max_acceleration));
                    arcmovecmd.Options.Feed = Math.Min(arcmovecmd.Options.Feed, maxfeed);
                }
            }
        }

        private MachineParameters config;
        public MoveFeedLimiter(MachineParameters config)
        {
            this.config = config;
        }
    }
}
