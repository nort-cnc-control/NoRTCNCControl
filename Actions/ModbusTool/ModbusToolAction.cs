using System;
using System.Threading;
using ModbusSender;
using System.Threading.Tasks;

namespace Actions.ModbusTool
{
    public class ModbusRegister
    {
        public UInt16 RegisterId { get; set; }
        public UInt16 RegisterValue { get; set; }
    }

    public class ModbusToolCommand
    {
        public ModbusRegister[] Registers;
        public int Delay;
    }

    public class ModbusToolAction : IAction
    {
        private readonly IModbusSender sender;
        private readonly ModbusToolCommand toolCommand;

        public ModbusToolAction(ModbusToolCommand toolCommand, IModbusSender sender)
        {
            this.sender = sender;
            this.toolCommand = toolCommand;
            ReadyToRun = new EventWaitHandle(true, EventResetMode.ManualReset);
            ContiniousBlockCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
            Started = new EventWaitHandle(false, EventResetMode.ManualReset);
            Finished = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        public bool RequireFinish => true;

        public EventWaitHandle ReadyToRun { get; private set; }

        public EventWaitHandle ContiniousBlockCompleted { get; private set; }

        public EventWaitHandle Started { get; private set; }

        public EventWaitHandle Finished { get; private set; }

        public bool Aborted => false;

        public bool Failed => false;

        public void Abort()
        {
        }

        public void Dispose()
        {
        }

        public void Run()
        {
            Started.Set();
            foreach (var reg in toolCommand.Registers)
            {
                sender.WriteRegister(reg.RegisterId, reg.RegisterValue);
            }
            if (toolCommand.Delay > 0)
                Task.Delay(toolCommand.Delay);
            Finished.Set();
            ContiniousBlockCompleted.Set();
        }
    }
}
