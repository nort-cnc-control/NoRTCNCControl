using System;
using System.Threading;

namespace Actions
{
    public interface IAction : IDisposable
    {
        event Action<IAction> EventStarted;
        event Action<IAction> EventFinished;

        bool RequireFinish { get; }

        #region status
        EventWaitHandle ReadyToRun { get; }
        EventWaitHandle ContiniousBlockCompleted { get; }
        EventWaitHandle Started { get; }
        EventWaitHandle Finished { get; }
        bool Aborted { get; }
        bool Failed { get; }
        #endregion

        #region methods
        void Run();
        void Abort();
        #endregion
    }
}
