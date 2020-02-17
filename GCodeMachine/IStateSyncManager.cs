using System;
using Vector;

namespace GCodeMachine
{
    public interface IStateSyncManager : IDisposable
    {
        void SyncCoordinates(Vector3 stateCoordinates);
    }
}
