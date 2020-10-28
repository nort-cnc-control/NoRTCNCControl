using System;
using RTSender;

namespace Actions.Tools
{
    public class GPIO_driver
    {
        private readonly int gpio;
        private readonly IRTSender sender;

        public GPIO_driver(IRTSender sender, int gpio)
        {
            this.gpio = gpio;
            this.sender = sender;
        }

        public IAction CreateAction(bool enable)
        {
            return new RTAction(sender, new RTGPIOCommand(gpio, enable));
        }
    }
}
