using System;
using System.Threading;
using ModbusSender;
using System.Threading.Tasks;

namespace Actions
{
    public class ModbusRegister
    {
        public int DeviceId { get; set; }
        public UInt16 RegisterId { get; set; }
        public UInt16 RegisterValue { get; set; }
    }

    public class ModbusCommand
    {
        public ModbusRegister[] Registers;
        public int Delay;
    }

    public class ModbusAction : IAction
    {
        private readonly IModbusSender sender;
        private readonly ModbusCommand toolCommand;

        public ModbusAction(ModbusCommand toolCommand, IModbusSender sender)
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

        public bool Aborted { get; private set; }

        public bool Failed => false;

        public event Action<IAction> EventStarted;
        public event Action<IAction> EventFinished;

        public void Abort()
        {
            Aborted = true;
        }

        public void Dispose()
        {
        }

        public void Run()
        {
            Started.Set();
            EventStarted?.Invoke(this);
            foreach (var reg in toolCommand.Registers)
            {
                sender.WriteRegister(reg.DeviceId, reg.RegisterId, reg.RegisterValue);
            }
            if (toolCommand.Delay > 0)
                Thread.Sleep(toolCommand.Delay);
            Finished.Set();
            ContiniousBlockCompleted.Set();
            EventFinished?.Invoke(this);
        }
    }
}
