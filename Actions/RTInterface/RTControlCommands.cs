using System;

namespace Actions
{
    public class RTBreakOnProbeCommand : IRTCommand
    {
        public String Command {get; private set;}

        public bool CommandIsCached { get { return false; } }

        public RTBreakOnProbeCommand(bool breakOnProbe)
        {
            if (breakOnProbe)
            {
                Command = "M996";
            }
            else
            {
                Command = "M995";
            }
        }
    }

    public class RTFailOnESCommand : IRTCommand
    {
        public String Command { get; private set; }

        public bool CommandIsCached { get { return false; } }

        public RTFailOnESCommand(bool fail)
        {
            if (fail)
            {
                Command = "M803";
            }
            else
            {
                Command = "M802";
            }
        }
    }

    public class RTLockCommand : IRTCommand
    {
        public String Command {get; private set;}

        public bool CommandIsCached { get { return false; } }

        public RTLockCommand(bool lockRT)
        {
            if (lockRT)
            {
                Command = "M801";
            }
            else
            {
                Command = "M800";
            }
        }
    }

    public class RTSetZeroCommand : IRTCommand
    {
        public String Command {get { return "M997"; }}

        public bool CommandIsCached { get { return false; } }
    }
}
