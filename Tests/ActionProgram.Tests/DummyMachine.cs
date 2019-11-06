using Machine;
using System;

namespace ActionProgram.Tests
{
    public class DummyMachine : IMachine
    {
        public State RunState { get; private set; }

        public DummyMachine()
        {
            RunState = State.Stopped;
        }

        public void Activate()
        {

        }

        public void Reboot()
        {
            Console.WriteLine("Reboot");
            RunState = State.Stopped;
        }

        public void Abort()
        {
            
        }

        public void Start()
        {
            Console.WriteLine("Starting");
            RunState = State.Running;
        }
        public void Stop()
        {
            Console.WriteLine("Stopping");
            RunState = State.Stopped;
        }
        public void Pause()
        {
            Console.WriteLine("Pause");
            RunState = State.Paused;
        }
        public void Continue()
        {
            Console.WriteLine("Continue");
            RunState = State.Running;
        }

        public void Dispose()
        {
        }
    }
}
