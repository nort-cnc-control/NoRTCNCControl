using System;
using System.Threading;

namespace Machine
{
    public enum State
    {
        Stopped,
        Running,
        Paused,
        Error,
    }

    public interface IMachine : IDisposable
    {
        State RunState { get; }
        void Reboot();
        void Abort();
        void Activate();
        void Start();
        void Stop();
        void Pause();
        void Continue();
    }
}
