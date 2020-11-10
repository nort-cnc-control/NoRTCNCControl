using System;
using System.Collections.Generic;
using Machine;

namespace Actions.Mills
{
    public class ManualMillManager : IMillManager
    {
        private readonly IMessageRouter router;
        private readonly IMachine machine;

        public ManualMillManager(IMessageRouter router, IMachine machine)
        {
            this.machine = machine;
            this.router = router;
        }

        public bool ToolChangeInterrupts => true;

        public void SelectMill(int millId)
        {
            var msg = string.Format("Please insert mill #{0}", millId);
            machine.Pause();
            Dictionary<string, string> message = new Dictionary<string, string>
            {
                ["type"] = "message",
                ["message"] = msg,
                ["message_type"] = "tool change",
            };
            router.Message(message);
        }
    }
}
