using System;

namespace Actions
{
    public class SimpleRTCommand : IRTCommand
    {
        public String Command {get; private set;}

        public bool CommandIsCached { get { return false; } }

        public SimpleRTCommand(String command)
        {
            this.Command = command;
        }
    }
}
