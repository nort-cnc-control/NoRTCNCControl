using System;
using Vector;

namespace Actions
{


    public interface IRTMoveCommand : IRTCommand
    {
        RTMovementOptions Options { get; }
        Vector3 Delta { get; }
        Vector3 DirStart { get; }
        Vector3 DirEnd { get; }
        double Length { get; }
    }
}
