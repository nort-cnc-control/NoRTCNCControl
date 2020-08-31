using System;
using System.Collections.Generic;
using Machine;

namespace Actions.Tools
{
    public class ManualToolManager : IToolManager
    {
        private readonly IMessageRouter router;
        private readonly IMachine machine;

        public ManualToolManager(IMessageRouter router, IMachine machine)
        {
            this.machine = machine;
            this.router = router;
        }

        public void SelectTool(int toolId)
        {
            var msg = string.Format("Please insert tool #{0}", toolId);
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
