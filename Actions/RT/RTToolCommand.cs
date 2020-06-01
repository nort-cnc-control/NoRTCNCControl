using System;
namespace Actions
{
    public class RTToolCommand : IRTCommand
    {
        private int tool;
        private bool enable;

        public RTToolCommand(int tool, bool enable)
        {
            this.tool = tool;
            this.enable = enable;
        }

        public bool CommandIsCached => true;

        public string Command
        {
            get
            {
                if (enable)
                    return String.Format("M3 D{0}", tool);
                else
                    return String.Format("M5 D{0}", tool);
            }
        }
    }
}
