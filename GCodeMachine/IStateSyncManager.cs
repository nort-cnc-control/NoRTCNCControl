using System;
using Actions;

namespace GCodeMachine
{
    public interface IStateSyncManager : IDisposable
    {
        void SyncCoordinates(Vector3 stateCoordinates);
    }
}
