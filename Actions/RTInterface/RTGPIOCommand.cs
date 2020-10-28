using System;
namespace Actions
{
    public class RTGPIOCommand : IRTCommand
    {
        private readonly int gpio;
        private readonly bool enable;

        public RTGPIOCommand(int gpio, bool enable)
        {
            this.gpio = gpio;
            this.enable = enable;
        }

        public bool CommandIsCached => true;

        public string Command
        {
            get
            {
                if (enable)
                    return String.Format("M3 D{0}", gpio);
                else
                    return String.Format("M5 D{0}", gpio);
            }
        }
    }
}
