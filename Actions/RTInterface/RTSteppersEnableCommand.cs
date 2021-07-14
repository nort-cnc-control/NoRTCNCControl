using System;
namespace Actions
{
    public class RTSteppersEnableCommand : IRTCommand
    {
        public bool CommandIsCached => false;

        public string Command { get; private set; }

        public RTSteppersEnableCommand(bool enable)
        {
            if (enable)
                Command = "M80";
            else
                Command = "M81";
        }
    }
}
